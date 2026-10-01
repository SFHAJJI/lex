using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Luxembourg;
using static Lex.V3.Ingest.Tests.EuAcquisitionTestFixture;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgAssertionSnapshotTests
{
    private const string Jolux = "http://data.legilux.public.lu/resource/ontology/jolux#";
    private static readonly SourceArtifactRef Run = Ref('1');
    private static readonly SourceArtifactRef Observation = Ref('2');
    private static readonly SourceArtifactRef[] Census = [Ref('3'), Ref('4')];
    private static readonly SourceArtifactRef[] Assertions = [Ref('5'), Ref('6')];

    [TestMethod]
    public async Task CrossRangeLookupPreservesCyclesSharedEdgesAndDuplicateAssertions()
    {
        var rows = new[] { Row("act", "isRealizedBy", "a"), Row("act", "isRealizedBy", "b"),
            Row("a", "isEmbodiedBy", "shared"), Row("b", "isEmbodiedBy", "shared"),
            Row("shared", "isExemplifiedBy", "act"), Row("shared", "title", "one"), Row("shared", "title", "one") };
        var snapshot = Open(await Hold(rows));
        Assert.AreEqual(4, snapshot.SubjectCount);
        Assert.AreEqual(7L, snapshot.AssertionCount);
        foreach (var subject in new[] { "act", "a", "b", "shared", "missing" })
            CollectionAssert.AreEqual(rows.Where(row => row.SubjectIri == subject).ToArray(), snapshot.ReadSubject(subject).ToArray());
        var expected = LuxembourgObservationDependencies.Collect("act", Observation,
            subject => rows.Where(row => row.SubjectIri == subject).ToArray());
        CollectionAssert.AreEqual(expected.ToArray(),
            LuxembourgObservationDependencies.Collect("act", Observation, snapshot.ReadSubject).ToArray());
    }

    [TestMethod]
    public async Task OriginalActLookupDoesNotTraverseItsExpression()
    {
        var rows = new[] {
            new LuxembourgObservedAssertion("consolidation", "http://www.w3.org/1999/02/22-rdf-syntax-ns#type",
                LuxembourgAssertionObjectKind.Iri, Jolux + "Consolidation", "", "", Observation),
            Row("consolidation", "isMemberOf", "work"), Row("work/jo", "isRealizedBy", "original-expression"),
            Row("original-expression", "isEmbodiedBy", "body") };
        var snapshot = Open(await Hold(rows));
        CollectionAssert.AreEqual(rows.Take(3).ToArray(),
            LuxembourgObservationDependencies.Collect("consolidation", Observation, snapshot.ReadSubject).ToArray());
    }

    [TestMethod]
    public async Task LiteralMetadataAndNonBmpSubjectsRoundTripExactly()
    {
        var rows = new[] { new LuxembourgObservedAssertion("urn:subject:\U0001D11E", Jolux + "title",
            LuxembourgAssertionObjectKind.Literal, "\u00c9tat\n\"quoted\" \u0000 \U0001D11E",
            "http://www.w3.org/2001/XMLSchema#string", "fr", Observation) };
        CollectionAssert.AreEqual(rows, Open(await Hold(rows)).ReadSubject(rows[0].SubjectIri).ToArray());
    }

    [TestMethod]
    public async Task EmptySnapshotKeepsItsBindingsWithoutInventingSubjects()
    {
        var snapshot = Open(await Hold([]));
        Assert.AreEqual(0, snapshot.SubjectCount);
        Assert.AreEqual(0L, snapshot.AssertionCount);
        Assert.HasCount(0, snapshot.ReadSubject("absent"));
    }

    [TestMethod]
    [DataRow("run")]
    [DataRow("observation")]
    [DataRow("census")]
    [DataRow("assertions")]
    [DataRow("proof-order")]
    public async Task ReopenRejectsChangedInputBinding(string change)
    {
        var artifact = await Hold([Row("act", "title", "value")]);
        Assert.ThrowsExactly<CustodyIntegrityException>(() => LuxembourgAssertionSnapshot.Open(artifact,
            change == "run" ? Ref('9') : Run, change == "observation" ? Ref('9') : Observation,
            change == "census" ? [Ref('9')] : Census,
            change == "assertions" ? [Ref('9')] : change == "proof-order" ? Assertions.Reverse().ToArray() : Assertions,
            CancellationToken.None));
    }

    [TestMethod]
    public void WriterRejectsAnotherObservation()
    {
        var row = new LuxembourgObservedAssertion("act", Jolux + "title", LuxembourgAssertionObjectKind.Literal,
            "value", "", "", Ref('9'));
        Assert.ThrowsExactly<InvalidOperationException>(() => LuxembourgAssertionSnapshot.Write(Stream.Null,
            Run, Observation, Census, Assertions, [row], CancellationToken.None));
    }

    [TestMethod]
    public async Task RepeatedNoncontiguousSubjectRefusesInsteadOfReplacingEarlierRows()
    {
        var artifact = await Hold([Row("a", "title", "first"), Row("b", "title", "middle"), Row("a", "title", "last")]);
        StringAssert.Contains(Assert.ThrowsExactly<CustodyIntegrityException>(() => Open(artifact)).Message,
            "Repeated assertion subject group");
    }

    [TestMethod]
    [DataRow("truncated-prefix")]
    [DataRow("negative-length")]
    [DataRow("oversized-length")]
    [DataRow("duplicate-property")]
    [DataRow("truncated-row")]
    public async Task RehashedMalformedSequencesStillRefuse(string fault)
    {
        using var output = new MemoryStream();
        _ = LuxembourgAssertionSnapshot.Write(output, Run, Observation, Census, Assertions,
            [Row("a", "title", "value")], CancellationToken.None);
        var bytes = output.ToArray();
        var firstRow = 4 + BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, 4));
        switch (fault)
        {
            case "truncated-prefix": bytes = bytes[..(firstRow + 2)]; break;
            case "negative-length": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(firstRow, 4), -1); break;
            case "oversized-length": BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(firstRow, 4),
                LuxembourgAssertionSnapshot.MaximumRecordBytes + 1); break;
            case "truncated-row": bytes = bytes[..^1]; break;
            case "duplicate-property":
                var row = Encoding.UTF8.GetString(bytes.AsSpan(firstRow + 4));
                var forged = Encoding.UTF8.GetBytes(row.Replace("{", "{\"Subject\":\"a\",", StringComparison.Ordinal));
                var prefix = bytes[..firstRow];
                bytes = new byte[firstRow + 4 + forged.Length];
                prefix.CopyTo(bytes, 0);
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(firstRow, 4), forged.Length);
                forged.CopyTo(bytes, firstRow + 4);
                break;
        }
        var artifact = await HoldBytes(bytes, CanonicalHash(bytes));
        Assert.ThrowsExactly<CustodyIntegrityException>(() => Open(artifact));
    }

    [TestMethod]
    public async Task CanonicalDigestMustMatchTheEntireParsedSequence()
    {
        using var output = new MemoryStream();
        _ = LuxembourgAssertionSnapshot.Write(output, Run, Observation, Census, Assertions,
            [Row("a", "title", "value")], CancellationToken.None);
        var artifact = await HoldBytes(output.ToArray(), new string('0', 64));
        StringAssert.Contains(Assert.ThrowsExactly<CustodyIntegrityException>(() => Open(artifact)).Message, "canonical digest");
    }

    [TestMethod]
    public async Task SeparateCompressedReaderReopensAcrossAChunkBoundaryAndRechecksLaterReads()
    {
        var directory = Directory.CreateTempSubdirectory("lex-v3-assertion-snapshot-");
        try
        {
            var rows = Enumerable.Range(0, 6).Select(index => new LuxembourgObservedAssertion("subject", Jolux + "title",
                LuxembourgAssertionObjectKind.Literal, new string((char)('a' + index), 1024 * 1024), "", "fr", Observation)).ToArray();
            var writer = FileSystemCustodyStore.WithBrotliCompression(directory.FullName);
            var (root, chunks) = await ChunkedDerivedArtifact.WriteAsync(writer, LuxembourgAssertionSnapshot.Kind,
                stream => LuxembourgAssertionSnapshot.Write(stream, Run, Observation, Census, Assertions, rows, CancellationToken.None), CancellationToken.None);
            Assert.IsGreaterThan(1, chunks.Count);
            var reader = FileSystemCustodyStore.WithBrotliCompression(directory.FullName);
            var artifact = await ChunkedDerivedArtifact.OpenAsync(reader, root.Reference.ContentSha256, LuxembourgAssertionSnapshot.Kind, CancellationToken.None);
            var snapshot = Open(artifact);
            CollectionAssert.AreEqual(rows, snapshot.ReadSubject("subject").ToArray());
            var tail = directory.EnumerateFiles(chunks[^1].Reference.ContentSha256 + ".br", SearchOption.AllDirectories).Single();
            File.WriteAllBytes(tail.FullName, [0]);
            Assert.ThrowsExactly<CustodyIntegrityException>(() => snapshot.ReadSubject("subject"));
        }
        finally { directory.Delete(recursive: true); }
    }

    [TestMethod]
    public async Task CancellationIsObservedAtOpenAndLaterLookup()
    {
        var artifact = await Hold([Row("a", "title", "value")]);
        using var stop = new CancellationTokenSource();
        var snapshot = LuxembourgAssertionSnapshot.Open(artifact, Run, Observation, Census, Assertions, stop.Token);
        stop.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => snapshot.ReadSubject("a"));
        Assert.ThrowsExactly<OperationCanceledException>(() => LuxembourgAssertionSnapshot.Open(artifact, Run, Observation, Census, Assertions, stop.Token));
    }

    [TestMethod]
    public void IndependentWritesEnumerateOnceAndProduceIdenticalBytes()
    {
        var calls = 0;
        IEnumerable<LuxembourgObservedAssertion> Rows() { calls++; yield return Row("a", "title", "first"); yield return Row("a", "title", "second"); }
        using var first = new MemoryStream();
        using var second = new MemoryStream();
        var firstHash = LuxembourgAssertionSnapshot.Write(first, Run, Observation, Census, Assertions, Rows(), CancellationToken.None);
        Assert.AreEqual(1, calls);
        var secondHash = LuxembourgAssertionSnapshot.Write(second, Run, Observation, Census, Assertions, Rows(), CancellationToken.None);
        Assert.AreEqual(2, calls);
        Assert.AreEqual(firstHash, secondHash);
        CollectionAssert.AreEqual(first.ToArray(), second.ToArray());
        Assert.AreEqual(CanonicalHash(first.ToArray()), firstHash);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AsyncRowsKeepCanonicalBytesAndReopenThroughSmallChunks(bool empty)
    {
        LuxembourgObservedAssertion[] rows = empty ? [] : Enumerable.Range(0, 13).Select(index =>
            new LuxembourgObservedAssertion(index < 7 ? "a" : "b", Jolux + "title",
                LuxembourgAssertionObjectKind.Literal, new string('x', 8192) + "\0é😀", "", "fr", Observation)).ToArray();
        using var expected = new MemoryStream();
        var expectedHash = LuxembourgAssertionSnapshot.Write(expected, Run, Observation, Census, Assertions,
            rows, CancellationToken.None);
        var passes = 0;
        var disposed = false;
        async IAsyncEnumerable<LuxembourgObservedAssertion> Source(
            [EnumeratorCancellation] CancellationToken token = default)
        {
            passes++;
            try
            {
                foreach (var row in rows)
                {
                    await Task.Yield();
                    token.ThrowIfCancellationRequested();
                    yield return row;
                }
            }
            finally { disposed = true; }
        }
        var store = new EuInMemoryCustodyStore();
        var (root, chunks) = await ChunkedDerivedArtifact.WriteSmallChunksAsync(store, LuxembourgAssertionSnapshot.Kind,
            (stream, token) => LuxembourgAssertionSnapshot.WriteAsync(stream, Run, Observation, Census, Assertions,
                Source(), token), CancellationToken.None);
        Assert.AreEqual(1, passes);
        Assert.IsTrue(disposed);
        if (!empty) Assert.IsGreaterThan(1, chunks.Count);
        var artifact = await ChunkedDerivedArtifact.OpenAsync(store, root.Reference.ContentSha256,
            LuxembourgAssertionSnapshot.Kind, CancellationToken.None);
        Assert.AreEqual(expectedHash, artifact.CanonicalSha256);
        using var reopenedBytes = artifact.OpenRead();
        using var actual = new MemoryStream();
        await reopenedBytes.CopyToAsync(actual);
        CollectionAssert.AreEqual(expected.ToArray(), actual.ToArray());
        var snapshot = Open(artifact);
        CollectionAssert.AreEqual(rows.Where(row => row.SubjectIri == "a").ToArray(), snapshot.ReadSubject("a").ToArray());
        CollectionAssert.AreEqual(rows.Where(row => row.SubjectIri == "b").ToArray(), snapshot.ReadSubject("b").ToArray());
    }

    [TestMethod]
    public async Task FailedCustodyCancelsAnAsyncSourceWaitingForItsNextRow()
    {
        var disposed = false;
        var sourceToken = CancellationToken.None;
        async IAsyncEnumerable<LuxembourgObservedAssertion> Source(
            [EnumeratorCancellation] CancellationToken token = default)
        {
            sourceToken = token;
            try
            {
                yield return new LuxembourgObservedAssertion("a", Jolux + "title",
                    LuxembourgAssertionObjectKind.Literal, new string('x', ChunkedDerivedArtifact.SmallChunkSize + 100),
                    "", "fr", Observation);
                await Task.Delay(Timeout.Infinite, token);
            }
            finally { disposed = true; }
        }
        var store = new EuInMemoryCustodyStore(failWriteDigest: (_, _) => true);
        await Assert.ThrowsExactlyAsync<CustodyRequiredException>(async () =>
            await ChunkedDerivedArtifact.WriteSmallChunksAsync(store, LuxembourgAssertionSnapshot.Kind,
                (stream, token) => LuxembourgAssertionSnapshot.WriteAsync(stream, Run, Observation, Census, Assertions,
                    Source(), token), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.IsTrue(disposed);
        Assert.IsTrue(sourceToken.IsCancellationRequested);
        Assert.AreEqual(1, store.CreateCallCount);
    }

    [TestMethod]
    public async Task AsyncSourceFailureAfterAChunkDoesNotPublishARoot()
    {
        async IAsyncEnumerable<LuxembourgObservedAssertion> Source()
        {
            yield return new LuxembourgObservedAssertion("a", Jolux + "title",
                LuxembourgAssertionObjectKind.Literal, new string('x', ChunkedDerivedArtifact.SmallChunkSize + 100),
                "", "fr", Observation);
            await Task.Yield();
            throw new InvalidOperationException("source failed after a chunk");
        }
        var store = new EuInMemoryCustodyStore();
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await ChunkedDerivedArtifact.WriteSmallChunksAsync(store, LuxembourgAssertionSnapshot.Kind,
                (stream, token) => LuxembourgAssertionSnapshot.WriteAsync(stream, Run, Observation, Census, Assertions,
                    Source(), token), CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.AreEqual("source failed after a chunk", error.Message);
        Assert.IsGreaterThan(0, store.CreateCallCount);
        foreach (var digest in store.WrittenDigestsInOrder)
        {
            var held = await store.ReadByDigestAsync(digest, CancellationToken.None);
            Assert.IsFalse(ChunkedDerivedArtifact.IsRoot(held.Span, out _));
        }
    }

    [TestMethod]
    [DataRow("wrong-observation")]
    [DataRow("null-row")]
    [DataRow("source-failure")]
    public async Task AsyncFailureDisposesTheSourceAndLeavesTheDestinationOpen(string fault)
    {
        var disposed = false;
        async IAsyncEnumerable<LuxembourgObservedAssertion> Source()
        {
            try
            {
                await Task.Yield();
                if (fault == "source-failure") throw new InvalidOperationException("source failed");
                yield return fault == "null-row" ? null! : new LuxembourgObservedAssertion("a", Jolux + "title",
                    LuxembourgAssertionObjectKind.Iri, "value", "", "", Ref('9'));
            }
            finally { disposed = true; }
        }
        using var output = new MemoryStream();
        Task Write() => LuxembourgAssertionSnapshot.WriteAsync(output, Run, Observation, Census, Assertions,
            Source(), CancellationToken.None);
        if (fault == "null-row") await Assert.ThrowsExactlyAsync<ArgumentNullException>(Write);
        else await Assert.ThrowsExactlyAsync<InvalidOperationException>(Write);
        Assert.IsTrue(disposed);
        Assert.IsTrue(output.CanWrite);
    }

    [TestMethod]
    public async Task CancellationBeforeAsyncWritingDoesNotOpenTheSourceOrWriteAHeader()
    {
        var opened = false;
        async IAsyncEnumerable<LuxembourgObservedAssertion> Source()
        {
            opened = true;
            await Task.Yield();
            yield return Row("a", "title", "value");
        }
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        using var output = new MemoryStream();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => LuxembourgAssertionSnapshot.WriteAsync(
            output, Run, Observation, Census, Assertions, Source(), stop.Token));
        Assert.IsFalse(opened);
        Assert.AreEqual(0L, output.Length);
        Assert.IsTrue(output.CanWrite);
    }

    private static async Task<ChunkedDerivedArtifact> Hold(IEnumerable<LuxembourgObservedAssertion> rows)
    {
        var store = new EuInMemoryCustodyStore();
        var (root, _) = await ChunkedDerivedArtifact.WriteAsync(store, LuxembourgAssertionSnapshot.Kind,
            stream => LuxembourgAssertionSnapshot.Write(stream, Run, Observation, Census, Assertions, rows, CancellationToken.None), CancellationToken.None);
        return await ChunkedDerivedArtifact.OpenAsync(store, root.Reference.ContentSha256, LuxembourgAssertionSnapshot.Kind, CancellationToken.None);
    }

    private static async Task<ChunkedDerivedArtifact> HoldBytes(byte[] bytes, string digest)
    {
        var store = new EuInMemoryCustodyStore();
        var (root, _) = await ChunkedDerivedArtifact.WriteAsync(store, LuxembourgAssertionSnapshot.Kind,
            stream => { stream.Write(bytes); return digest; }, CancellationToken.None);
        return await ChunkedDerivedArtifact.OpenAsync(store, root.Reference.ContentSha256, LuxembourgAssertionSnapshot.Kind, CancellationToken.None);
    }

    private static LuxembourgAssertionSnapshot Open(ChunkedDerivedArtifact artifact) =>
        LuxembourgAssertionSnapshot.Open(artifact, Run, Observation, Census, Assertions, CancellationToken.None);
    private static LuxembourgObservedAssertion Row(string subject, string predicate, string value) =>
        new(subject, Jolux + predicate, LuxembourgAssertionObjectKind.Iri, value, "", "", Observation);
    private static SourceArtifactRef Ref(char digit) => new("urn:uuid:00000000-0000-4000-8000-000000000001", new string(digit, 64));
    private static string CanonicalHash(byte[] bytes)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(LuxembourgAssertionSnapshot.Kind + "\n"));
        hash.AppendData(bytes);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
