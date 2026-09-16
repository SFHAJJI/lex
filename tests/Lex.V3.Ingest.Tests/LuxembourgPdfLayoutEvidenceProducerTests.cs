using System.Reflection;
using System.Security.Cryptography;
using Lex.V3.Contracts.Custody;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgPdfLayoutEvidenceProducerTests
{
    [TestMethod]
    public async Task RealPublisherPdfsRetainImagesRenderingModesAndCanonicalIdentity()
    {
        var consolidated = Fixture("lu-pdf-consolidated-2020-04-08-a265.bin", 84_897,
            "86b5d5021ebd57735914a2a02ea0158447eb2b2d6a45889fbd42faea2bd973ea");
        var invisible = Fixture("lu-pdf-invisible-1987-12-23-n5.bin", 237_720,
            "9f34cd405f1f0f9b4349fa967b2271b257d4982fbf90351f4b6261f56633c111");

        var clean = await ProduceOneAsync(consolidated);
        var cleanDocument = await clean.Store.OpenAsync(clean.Outcome);
        Assert.HasCount(1, cleanDocument.Pages);
        Assert.HasCount(1_462, cleanDocument.Glyphs);
        Assert.AreEqual(0, cleanDocument.Pages.Sum(static page => page.ImageCount));
        Assert.AreEqual(0, InvisibleCount(cleanDocument));
        Assert.AreEqual("a39876abcccef29a0f22fc53468401ec28462f408c70e7792a7fcd06ac1b255f",
            clean.Outcome.SemanticIdentitySha256,
            "A field omission or reordering must move the canonical admitted identity.");

        var hidden = await ProduceOneAsync(invisible);
        var hiddenDocument = await hidden.Store.OpenAsync(hidden.Outcome);
        Assert.AreEqual(58, hiddenDocument.Pages.Sum(static page => page.ImageCount));
        Assert.AreEqual(27, InvisibleCount(hiddenDocument));

        var imageBearing = await ProduceOneAsync(LuxembourgDocumentFetchFixtures.PdfBody());
        var imageDocument = await imageBearing.Store.OpenAsync(imageBearing.Outcome);
        Assert.AreEqual(9, imageDocument.Pages.Sum(static page => page.ImageCount));
        Assert.AreEqual(0, InvisibleCount(imageDocument));
    }

    [TestMethod]
    public async Task MixedPopulationPreservesSourceOrderAndReadsEachExactReceipt()
    {
        var firstBytes = Fixture("lu-pdf-consolidated-2020-04-08-a265.bin", 84_897,
            "86b5d5021ebd57735914a2a02ea0158447eb2b2d6a45889fbd42faea2bd973ea");
        var secondBytes = Fixture("lu-pdf-invisible-1987-12-23-n5.bin", 237_720,
            "9f34cd405f1f0f9b4349fa967b2271b257d4982fbf90351f4b6261f56633c111");
        var first = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync(firstBytes, "first"));
        var second = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync(secondBytes, "second"));
        var source = new LuxembourgPdfProfileEligibilityPopulation(
            first.SourceComposition,
            [first.Outcomes.Single(), second.Outcomes.Single()],
            LuxembourgPdfProfileEligibilityProducer.RuleProfileSha256);
        var store = ReadStore.Create(
            (first.Outcomes.Single().TransportReceipt.Reference, firstBytes),
            (second.Outcomes.Single().TransportReceipt.Reference, secondBytes));

        var result = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(source, CancellationToken.None);

        Assert.IsTrue(result.Produced, $"{result.Refusal}: {result.Detail}");
        Assert.HasCount(2, result.Population!.Outcomes);
        CollectionAssert.AreEqual(
            source.Outcomes.Select(static value => value.PublisherItemIri).ToArray(),
            result.Population.Outcomes.Select(static value => value.SourceEligibility.PublisherItemIri).ToArray());
        CollectionAssert.AreEqual(
            source.Outcomes.Select(static value => value.TransportReceipt.Reference.ContentSha256).ToArray(),
            store.TransportReads.Take(2).ToArray(),
            "Each member must reopen its own source receipt, in proof-bound source order.");
        var firstDocument = await store.OpenAsync(result.Population.Outcomes[0]);
        var secondDocument = await store.OpenAsync(result.Population.Outcomes[1]);
        Assert.AreEqual(0, firstDocument.Pages.Sum(static page => page.ImageCount));
        Assert.AreEqual(58, secondDocument.Pages.Sum(static page => page.ImageCount));
        Assert.AreEqual(27, InvisibleCount(secondDocument));
    }

    [TestMethod]
    public async Task ImageOnlyPdfIsAZeroGlyphGapWhosePagesSurvive()
    {
        var produced = await ProduceOneAsync(
            EuImageOnlyAnnexProducerTests.MinimalPdf(image: true, text: false));

        Assert.AreEqual(LuxembourgPdfLayoutEvidenceDisposition.TypedGap, produced.Outcome.Disposition);
        Assert.AreEqual(LuxembourgPdfLayoutEvidenceGapReason.NoTextGlyphs, produced.Outcome.GapReason);
        Assert.AreEqual(1, produced.Outcome.PageCount);
        Assert.AreEqual(0, produced.Outcome.GlyphCount);
        Assert.IsNotNull(produced.Outcome.LayoutEvidenceReceipt);
        var document = await produced.Store.OpenAsync(produced.Outcome);
        Assert.HasCount(1, document.Pages);
        Assert.AreEqual(1, document.Pages.Single().ImageCount);
        Assert.IsEmpty(document.Glyphs);
    }

    [TestMethod]
    public async Task ArtifactCeilingStopsEncodingAsAnExplicitMemberGap()
    {
        var bytes = Fixture("lu-pdf-consolidated-2020-04-08-a265.bin", 84_897,
            "86b5d5021ebd57735914a2a02ea0158447eb2b2d6a45889fbd42faea2bd973ea");
        var source = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync(bytes));
        var store = ReadStore.Create((source.Outcomes.Single().TransportReceipt.Reference, bytes));

        var result = await new LuxembourgPdfLayoutEvidenceProducer(store, maximumArtifactBytes: 64)
            .RunAsync(source, CancellationToken.None);

        Assert.IsTrue(result.Produced, $"{result.Refusal}: {result.Detail}");
        var outcome = result.Population!.Outcomes.Single();
        Assert.AreEqual(LuxembourgPdfLayoutEvidenceDisposition.TypedGap, outcome.Disposition);
        Assert.AreEqual(LuxembourgPdfLayoutEvidenceGapReason.EvidenceArtifactTooLarge, outcome.GapReason);
        Assert.IsNull(outcome.LayoutEvidenceReceipt);
        Assert.AreEqual(0, store.CreateCount, "An oversized artifact must stop before custody.");
    }

    [TestMethod]
    public async Task UnreadableEligiblePdfIsAnExplicitGapRatherThanInventedText()
    {
        var bytes = "%PDF-1.7 deliberately unreadable\n%%EOF\n"u8.ToArray();
        var source = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync(bytes));
        var store = ReadStore.Create((source.Outcomes.Single().TransportReceipt.Reference, bytes));

        var result = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(source, CancellationToken.None);

        Assert.IsTrue(result.Produced, $"{result.Refusal}: {result.Detail}");
        var outcome = result.Population!.Outcomes.Single();
        Assert.AreEqual(LuxembourgPdfLayoutEvidenceDisposition.TypedGap, outcome.Disposition);
        Assert.AreEqual(LuxembourgPdfLayoutEvidenceGapReason.PdfUnreadable, outcome.GapReason);
        Assert.IsNull(outcome.LayoutEvidenceReceipt);
        Assert.AreEqual(0, outcome.PageCount);
        Assert.AreEqual(0, outcome.GlyphCount);
    }

    [TestMethod]
    public async Task MissingRetainedBytesRefuseTheWholePopulation()
    {
        var bytes = LuxembourgDocumentFetchFixtures.PdfBody();
        var source = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync(bytes));
        var result = await new LuxembourgPdfLayoutEvidenceProducer(ReadStore.Create())
            .RunAsync(source, CancellationToken.None);

        Assert.IsFalse(result.Produced);
        Assert.IsNull(result.Population);
        Assert.AreEqual(LuxembourgPdfLayoutEvidenceProductionRefusal.RetainedBytesUnavailable, result.Refusal);
    }

    [TestMethod]
    public async Task ArtifactCustodyFailureRefusesTheWholePopulation()
    {
        var bytes = LuxembourgDocumentFetchFixtures.PdfBody();
        var source = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync(bytes));
        var store = ReadStore.Create((source.Outcomes.Single().TransportReceipt.Reference, bytes));
        store.FailOnCreate = true;

        var result = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(source, CancellationToken.None);

        Assert.IsFalse(result.Produced);
        Assert.IsNull(result.Population);
        Assert.AreEqual(
            LuxembourgPdfLayoutEvidenceProductionRefusal.LayoutEvidenceCustodyUnavailable,
            result.Refusal);
    }

    [TestMethod]
    public async Task NonPdfAndUpstreamGapRemainExplicitAndNeedNoCustodyRead()
    {
        var xml = await EligibilityAsync(
            await LuxembourgGazetteAcquisitionTests.CompleteXmlForStage3BodyCompositionAsync());
        var upstreamGap = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompleteWithDistinctReceiptsForStage3BodyCompositionAsync());
        var store = ReadStore.Create();
        store.FailOnRead = true;

        var xmlResult = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(xml, CancellationToken.None);
        var gapResult = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(upstreamGap, CancellationToken.None);

        Assert.AreEqual(LuxembourgPdfLayoutEvidenceDisposition.NotApplicable,
            xmlResult.Population!.Outcomes.Single().Disposition);
        Assert.AreEqual(LuxembourgPdfLayoutEvidenceGapReason.UpstreamEligibilityGap,
            gapResult.Population!.Outcomes.Single().GapReason);
    }

    [TestMethod]
    public async Task EqualPublisherEvidenceHasStableIdentityAcrossRuns()
    {
        var bytes = LuxembourgDocumentFetchFixtures.PdfBody();
        var firstSource = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync(bytes));
        var secondSource = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync(bytes));
        var store = ReadStore.Create(
            (firstSource.Outcomes.Single().TransportReceipt.Reference, bytes),
            (secondSource.Outcomes.Single().TransportReceipt.Reference, bytes));

        var first = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(firstSource, CancellationToken.None);
        var second = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(secondSource, CancellationToken.None);

        Assert.AreEqual(first.Population!.IdentitySha256, second.Population!.IdentitySha256);
        Assert.AreEqual(first.Population.Outcomes.Single().SemanticIdentitySha256,
            second.Population.Outcomes.Single().SemanticIdentitySha256);
        Assert.AreEqual(first.Population.Outcomes.Single().LayoutEvidenceReceipt!.Reference.ContentSha256,
            second.Population.Outcomes.Single().LayoutEvidenceReceipt!.Reference.ContentSha256);
    }

    [TestMethod]
    public void PublicDoorAcceptsOnlyTheProofCompleteEligibilityPopulation()
    {
        var parameters = typeof(LuxembourgPdfLayoutEvidenceProducer)
            .GetMethod(nameof(LuxembourgPdfLayoutEvidenceProducer.RunAsync))!
            .GetParameters().Select(static parameter => parameter.ParameterType).ToArray();
        CollectionAssert.AreEqual(
            new[] { typeof(LuxembourgPdfProfileEligibilityPopulation), typeof(CancellationToken) },
            parameters);
    }

    private static int InvisibleCount(LuxembourgPdfLayoutEvidenceDocument document) =>
        document.Glyphs.Count(static glyph => glyph.RenderingMode == 3);

    private static async Task<(LuxembourgPdfLayoutEvidenceOutcome Outcome, ReadStore Store)>
        ProduceOneAsync(byte[] bytes)
    {
        var source = await EligibilityAsync(await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync(bytes));
        var store = ReadStore.Create((source.Outcomes.Single().TransportReceipt.Reference, bytes));
        var result = await new LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(source, CancellationToken.None);
        Assert.IsTrue(result.Produced, $"{result.Refusal}: {result.Detail}");
        return (result.Population!.Outcomes.Single(), store);
    }

    private static byte[] Fixture(string fileName, int expectedLength, string expectedSha256)
    {
        var bytes = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "LuDocumentFetch", fileName));
        Assert.AreEqual(expectedLength, bytes.Length);
        Assert.AreEqual(expectedSha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        return bytes;
    }

    private static async Task<LuxembourgPdfProfileEligibilityPopulation> EligibilityAsync(
        LuxembourgQueryExecutionResult luxembourg)
    {
        var acquired = await EuFormexAnnexClassificationReconciliationTests.AcquiredFixtureAsync();
        var europe = new[]
            {
                acquired.Classification.Binding.FormexSource.ObjectRef,
                acquired.Classification.Binding.XhtmlSource.ObjectRef,
                acquired.Classification.Binding.PdfSource.ObjectRef,
            }.Aggregate(acquired.Run, Stage3EvidenceLineageTests.AddEuropeCorpusRecord);
        var formex = EuFormexAnnexClassificationReconciliationTests.Reconciliation(
            europe, [acquired.Outcome]);
        var classifications = Stage3EvidenceEnvelopeTests.CompleteClassifications(
            formex, [acquired.Classification]);
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(europe, luxembourg);
        var envelope = Stage3EvidenceEnvelopeTests.TryCreate(
            europe, luxembourg, formex, classifications, fidelity,
            out var envelopeRefusal, out var envelopeDetail);
        Assert.IsNotNull(envelope, $"{envelopeRefusal}: {envelopeDetail}");
        var composition = Stage3BodyComposition.TryCreate(envelope, out var refusal, out var detail);
        Assert.IsNotNull(composition, $"{refusal}: {detail}");
        return LuxembourgPdfProfileEligibilityProducer.Produce(composition);
    }

    private sealed class ReadStore : ICustodyStore
    {
        private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

        internal bool FailOnRead { get; set; }
        internal bool FailOnCreate { get; set; }
        internal int CreateCount { get; private set; }
        internal List<string> TransportReads { get; } = [];

        internal static ReadStore Create(params (DurableBlobRef Reference, byte[] Bytes)[] retained)
        {
            var store = new ReadStore();
            foreach (var (reference, bytes) in retained)
            {
                Assert.AreEqual(reference.ByteLength, bytes.LongLength);
                Assert.AreEqual(reference.ContentSha256, CustodyDigest.Of(bytes));
                store._objects[reference.ContentSha256] = bytes.ToArray();
            }
            return store;
        }

        internal Task<LuxembourgPdfLayoutEvidenceDocument> OpenAsync(
            LuxembourgPdfLayoutEvidenceOutcome outcome) =>
            LuxembourgPdfLayoutEvidenceArtifactReader.ReadAsync(outcome, this, CancellationToken.None);

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes, CustodyClass custodyClass, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailOnCreate) throw new CustodyRequiredException("fixture write unavailable");
            CreateCount++;
            var frozen = bytes.ToArray();
            var digest = CustodyDigest.Of(frozen);
            _objects[digest] = frozen;
            var reference = new DurableBlobRef(
                CustodySchemaIds.DurableBlobRef, digest, frozen.LongLength, custodyClass);
            var observedAt = new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero);
            var policy = new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                reference,
                CustodyVerificationProfile.ImmutableObject1,
                Guid.Parse("00000000-0000-0000-0000-000000000660"),
                CustodyProtection.LockedTime,
                observedAt,
                observedAt.AddDays(91));
            return Task.FromResult(new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt, reference, policy));
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailOnRead) throw new AssertFailedException("This path must not touch custody.");
            if (!_objects.TryGetValue(reference.ContentSha256, out var bytes))
                throw new CustodyRequiredException("fixture bytes unavailable");
            TransportReads.Add(reference.ContentSha256);
            return Task.FromResult<ReadOnlyMemory<byte>>(bytes);
        }

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(
            string contentSha256, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_objects.TryGetValue(contentSha256, out var bytes))
                throw new CustodyRequiredException("fixture bytes unavailable");
            return Task.FromResult<ReadOnlyMemory<byte>>(bytes);
        }
    }
}
