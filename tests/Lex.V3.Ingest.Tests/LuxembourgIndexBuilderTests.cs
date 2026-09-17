using Lex.V3.Contracts;
using Lex.V3.Contracts.Index;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgIndexBuilderTests
{
    [TestMethod]
    public void BuilderAndStrictReaderShipAsOneTerminalSlice()
    {
        var schema = (string)typeof(LuxembourgIndexBuilder)
            .GetField(nameof(LuxembourgIndexBuilder.Schema))!
            .GetRawConstantValue()!;
        Assert.AreEqual("lex-v3-luxembourg-index/1", schema);
        Assert.IsNotNull(typeof(LuxembourgIndexBuilder).GetMethod(nameof(LuxembourgIndexBuilder.TryBuild)));
        Assert.IsNotNull(typeof(LuxembourgIndexReader).GetMethod(nameof(LuxembourgIndexReader.OpenAndVerify)));
    }

    [TestMethod]
    public async Task CompleteEnvelopeBuildsDeterministicLuOnlyIndexAndMeasuredManifest()
    {
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync();
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");

        var first = LuxembourgIndexBuilder.TryBuild(envelope, out var firstRefusal, out var firstDetail);
        var second = LuxembourgIndexBuilder.TryBuild(envelope, out var secondRefusal, out var secondDetail);

        Assert.IsNotNull(first, $"{firstRefusal}: {firstDetail}");
        Assert.IsNotNull(second, $"{secondRefusal}: {secondDetail}");
        CollectionAssert.AreEqual(first.IndexBytes.ToArray(), second.IndexBytes.ToArray());
        Assert.AreEqual(first.IndexRef, second.IndexRef);
        Assert.AreEqual(first.CapabilityManifestRef, second.CapabilityManifestRef);
        CollectionAssert.AreEqual(
            first.CapabilityManifestBytes.ToArray(),
            second.CapabilityManifestBytes.ToArray());
        CollectionAssert.AreEqual(
            first.CapabilityManifest.Cells.ToArray(),
            second.CapabilityManifest.Cells.ToArray());
        Assert.AreEqual(PublisherId.LuLegilux, first.CapabilityManifest.Publisher);
        Assert.AreEqual(first.IndexRef.Sha256, first.CapabilityManifest.IndexSha256);
        var reopenedManifest = V3IndexCapabilityManifestArtifact.ParseAndVerify(
            first.CapabilityManifestRef,
            first.CapabilityManifestBytes.Span,
            PublisherId.LuLegilux,
            first.IndexRef.Sha256);
        CollectionAssert.AreEqual(
            first.CapabilityManifest.Cells.ToArray(),
            reopenedManifest.Cells.ToArray());

        using var reader = LuxembourgIndexReader.OpenAndVerify(
            first.IndexRef,
            first.IndexBytes.Span,
            corpus.ArtifactRef,
            first.CapabilityManifest);
        var expectedLuMembers = corpus.VerifiedSet.Set.Members.Count(static member =>
            member.Publisher == PublisherId.LuLegilux);
        Assert.AreEqual(expectedLuMembers, reader.MemberCount);
        Assert.IsTrue(reader.MemberCount < corpus.VerifiedSet.Set.Members.Count,
            "The Luxembourg index must not contain the EU population.");
        var unsupported = reader.Search(
            "deu", new DateOnly(1900, 1, 1), new DateOnly(2100, 1, 1), "law");
        Assert.AreEqual(
            V3IndexCapabilityLookupOutcome.FilterNotSupportedByIndex,
            unsupported.Outcome);
        Assert.IsEmpty(unsupported.ArticleIdentities);
    }

    [TestMethod]
    public async Task CorpusIncompletenessRefusesBeforeAnIndexExists()
    {
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync(includeLegalNotice: false);

        var result = LuxembourgIndexBuilder.TryBuild(envelope, out var refusal, out var detail);

        Assert.IsNull(result);
        Assert.AreEqual(LuxembourgIndexBuildRefusal.CorpusRefused, refusal, detail);
    }

    [TestMethod]
    public async Task StrictReaderRejectsByteCorpusAndCapabilitySubstitution()
    {
        var envelope = await LexCorpus6BuilderTests.CompleteProfileEnvelopeAsync();
        var corpus = LexCorpus6Builder.TryBuild(envelope, out var corpusRefusal, out var corpusDetail);
        Assert.IsNotNull(corpus, $"{corpusRefusal}: {corpusDetail}");
        var built = LuxembourgIndexBuilder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");

        var changedBytes = built.IndexBytes.ToArray();
        changedBytes[^1] ^= 1;
        Assert.ThrowsExactly<ArgumentException>(() => LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, changedBytes, corpus.ArtifactRef, built.CapabilityManifest));

        var otherCorpus = new SourceArtifactRef(corpus.ArtifactRef.ResourceId, new string('a', 64));
        Assert.ThrowsExactly<InvalidDataException>(() => LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, otherCorpus, built.CapabilityManifest));

        Assert.IsTrue(V3IndexCapabilityManifest.TryCreate(
            PublisherId.LuLegilux,
            new string('b', 64),
            [],
            out var wrongManifest,
            out _));
        Assert.ThrowsExactly<ArgumentException>(() => LuxembourgIndexReader.OpenAndVerify(
            built.IndexRef, built.IndexBytes.Span, corpus.ArtifactRef, wrongManifest!));
    }
}
