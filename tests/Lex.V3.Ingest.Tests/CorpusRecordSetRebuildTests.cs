using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Scope;
using static Lex.V3.Ingest.Tests.EuAcquisitionTestFixture;

namespace Lex.V3.Ingest.Tests;

public sealed partial class CorpusRecordSetWriterTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task RebuildPreservesOriginalSetIdentityAndCanonicalBytesInIndependentStores(bool chunked, bool weaker)
    {
        var manifest = RebuildManifest(chunked);
        var original = await new CorpusRecordSetWriter(new EuInMemoryCustodyStore()).WriteAsync(
            manifest, ManifestRef(), RunIdentity(), null, CancellationToken.None);
        Assert.IsNull(original.Refusal, original.Refusal?.Detail);
        byte[]? firstBytes = null;
        foreach (var store in new[] { new EuInMemoryCustodyStore(unenforceDigest: _ => weaker), new EuInMemoryCustodyStore(unenforceDigest: _ => weaker) })
        {
            var rebuilt = await new CorpusRecordSetWriter(store).RebuildAsync(manifest, ManifestRef(), RunIdentity(), null,
                original.SetRef!, CancellationToken.None);
            Assert.IsNull(rebuilt.Refusal, rebuilt.Refusal?.Detail);
            Assert.AreEqual(original.SetRef, rebuilt.SetRef);
            Assert.AreEqual(weaker ? CustodyMembership.RetainedUnenforced : CustodyMembership.Floored, rebuilt.RetainedFloor);
            var reopened = await new CorpusRecordSetReader(store).ReadAsync(rebuilt.RetainedSetReceipt!, rebuilt.SetRef!, CancellationToken.None);
            Assert.IsNull(reopened.Refusal, reopened.Refusal?.Detail);
            var bytes = Canonical(reopened.VerifiedSet!);
            CollectionAssert.AreEqual(Canonical(original.VerifiedSet!), bytes);
            if (firstBytes is not null) CollectionAssert.AreEqual(firstBytes, bytes);
            firstBytes = bytes;
            var root = await store.ReadByDigestAsync(rebuilt.RetainedSetReceipt!.Reference.ContentSha256, CancellationToken.None);
            Assert.AreEqual(chunked, ChunkedDerivedArtifact.IsRoot(root.Span, out var kind),
                "both inline and chunked storage branches must be exercised");
            if (chunked) Assert.AreEqual(CorpusRecordSetWriter.ChunkedKind, kind);
        }
    }

    [TestMethod]
    [DataRow(false, "digest")]
    [DataRow(true, "digest")]
    [DataRow(false, "manifest_ref")]
    [DataRow(true, "manifest_ref")]
    [DataRow(false, "run")]
    [DataRow(true, "run")]
    [DataRow(false, "outcome")]
    [DataRow(true, "outcome")]
    public async Task RebuildRefusesChangedInputsBeforeAnyCustodyWrite(bool chunked, string changed)
    {
        var manifest = RebuildManifest(chunked);
        var original = await new CorpusRecordSetWriter(new EuInMemoryCustodyStore()).WriteAsync(
            manifest, ManifestRef(), RunIdentity(), null, CancellationToken.None);
        Assert.IsNull(original.Refusal, original.Refusal?.Detail);
        var store = new EuInMemoryCustodyStore();
        var reference = changed == "digest" ? new SourceArtifactRef(original.SetRef!.ResourceId, new string('a', 64)) : original.SetRef!;
        var run = changed == "run" ? new SourceArtifactRef("urn:uuid:00000000-0000-4000-8000-000000000998", RunIdentity().Sha256) : RunIdentity();
        var outcomes = changed == "outcome" ? new Dictionary<int, CorpusAcquisitionOutcome>
            { [0] = CorpusAcquisitionOutcome.Refused(CorpusAcquisitionRefusalReason.StatusContentForbidden) } : null;
        var result = await new CorpusRecordSetWriter(store).RebuildAsync(manifest,
            changed == "manifest_ref" ? OtherManifestRef() : ManifestRef(), run, outcomes, reference, CancellationToken.None);
        Assert.AreEqual(CorpusRecordSetWriteRefusalKind.RebuildIdentityDisagrees, result.Refusal?.Kind);
        Assert.IsNull(result.VerifiedSet);
        Assert.IsNull(result.SetRef);
        Assert.AreEqual(0, store.CreateCallCount);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RebuildStillRequiresAnActualCheckedHold(bool chunked)
    {
        var manifest = RebuildManifest(chunked);
        var original = await new CorpusRecordSetWriter(new EuInMemoryCustodyStore()).WriteAsync(
            manifest, ManifestRef(), RunIdentity(), null, CancellationToken.None);
        var store = new EuInMemoryCustodyStore(failWriteDigest: (_, _) => true);
        var result = await new CorpusRecordSetWriter(store).RebuildAsync(manifest, ManifestRef(), RunIdentity(), null,
            original.SetRef!, CancellationToken.None);
        Assert.AreEqual(CorpusRecordSetWriteRefusalKind.RecordSetNotRetained, result.Refusal?.Kind);
        Assert.IsNull(result.VerifiedSet);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RebuildCancellationPreventsWrites(bool chunked)
    {
        var store = new EuInMemoryCustodyStore();
        using var source = new CancellationTokenSource(); source.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => new CorpusRecordSetWriter(store).RebuildAsync(
            RebuildManifest(chunked), ManifestRef(), RunIdentity(), null, ManifestRef(), source.Token));
        Assert.AreEqual(0, store.CreateCallCount);
    }

    private static ScopeManifest RebuildManifest(bool chunked) => chunked ? LargeManifestFixture() : ManifestFixture();
}
