using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class Stage3EvidenceLineageTests
{
    [TestMethod]
    public async Task RealEnvelopeBindsTwoSeparateEvidenceDerivedRunCoordinates()
    {
        var envelope = await CompleteEnvelopeAsync();

        var lineage = Stage3EvidenceLineage.TryBind(envelope, out var refusal, out var detail);

        Assert.AreEqual(Stage3EvidenceLineageRefusal.None, refusal, detail);
        Assert.IsNotNull(lineage);
        Assert.AreSame(envelope, lineage.Envelope);
        Assert.AreEqual(envelope.Europe.CorpusRecordSetRef, lineage.EuropeCorpusRecordSetRef);
        Assert.AreEqual(envelope.Europe.CorpusRecordSet!.Set.ManifestRef, lineage.EuropeManifestRef);
        Assert.AreEqual(envelope.Europe.CorpusRecordSet.Set.RunIdentity, lineage.EuropeRunIdentity);
        Assert.AreEqual(envelope.Luxembourg.CorpusRecordSetRef, lineage.LuxembourgCorpusRecordSetRef);
        Assert.AreEqual(envelope.Luxembourg.CorpusRecordSet!.Set.ManifestRef, lineage.LuxembourgManifestRef);
        Assert.AreEqual(envelope.Luxembourg.CorpusRecordSet.Set.RunIdentity, lineage.LuxembourgRunIdentity);
        Assert.AreNotEqual(lineage.EuropeRunIdentity, lineage.LuxembourgRunIdentity);
    }

    [TestMethod]
    public async Task EuropeCorpusSetReferenceCannotBeSubstituted()
    {
        var original = await CompleteEnvelopeAsync();
        var envelope = Rebuild(
            CopyEurope(original.Europe, corpusRecordSetRef: original.Luxembourg.CorpusRecordSetRef),
            original.Luxembourg);

        Assert.IsNull(Stage3EvidenceLineage.TryBind(envelope, out var refusal, out _));
        Assert.AreEqual(Stage3EvidenceLineageRefusal.EuropeCorpusRecordSetMismatch, refusal);
    }

    [TestMethod]
    public async Task EuropeManifestDigestCannotBeSubstituted()
    {
        var original = await CompleteEnvelopeAsync();
        var envelope = Rebuild(
            CopyEurope(original.Europe, manifestCanonicalSha256:
                original.Luxembourg.ScopeManifestCanonicalSha256),
            original.Luxembourg);

        Assert.IsNull(Stage3EvidenceLineage.TryBind(envelope, out var refusal, out _));
        Assert.AreEqual(Stage3EvidenceLineageRefusal.EuropeManifestMismatch, refusal);
    }

    [TestMethod]
    public async Task EuropeRetainedManifestBytesCannotBeSubstituted()
    {
        var original = await CompleteEnvelopeAsync();
        var envelope = Rebuild(
            CopyEurope(original.Europe, scopeManifestReceipt: original.Luxembourg.ScopeManifestReceipt),
            original.Luxembourg);

        Assert.IsNull(Stage3EvidenceLineage.TryBind(envelope, out var refusal, out _));
        Assert.AreEqual(Stage3EvidenceLineageRefusal.EuropeRunIdentityMismatch, refusal);
    }

    [TestMethod]
    public async Task LuxembourgCorpusSetReferenceCannotBeSubstituted()
    {
        var original = await CompleteEnvelopeAsync();
        var envelope = Rebuild(
            original.Europe,
            CopyLuxembourg(original.Luxembourg, corpusRecordSetRef: original.Europe.CorpusRecordSetRef));

        Assert.IsNull(Stage3EvidenceLineage.TryBind(envelope, out var refusal, out _));
        Assert.AreEqual(Stage3EvidenceLineageRefusal.LuxembourgCorpusRecordSetMismatch, refusal);
    }

    [TestMethod]
    public async Task LuxembourgManifestDigestCannotBeSubstituted()
    {
        var original = await CompleteEnvelopeAsync();
        var envelope = Rebuild(
            original.Europe,
            CopyLuxembourg(original.Luxembourg, manifestCanonicalSha256:
                original.Europe.ScopeManifestCanonicalSha256));

        Assert.IsNull(Stage3EvidenceLineage.TryBind(envelope, out var refusal, out _));
        Assert.AreEqual(Stage3EvidenceLineageRefusal.LuxembourgManifestMismatch, refusal);
    }

    [TestMethod]
    public async Task LuxembourgRetainedManifestBytesCannotBeSubstituted()
    {
        var original = await CompleteEnvelopeAsync();
        var envelope = Rebuild(
            original.Europe,
            CopyLuxembourg(original.Luxembourg, scopeManifestReceipt: original.Europe.ScopeManifestReceipt));

        Assert.IsNull(Stage3EvidenceLineage.TryBind(envelope, out var refusal, out _));
        Assert.AreEqual(Stage3EvidenceLineageRefusal.LuxembourgRunIdentityMismatch, refusal);
    }

    [TestMethod]
    public async Task ImageOnlyAnnexSourceInTheBoundEuropeCorpusBinds()
    {
        var europe = await EuAxiomWiringHarness.RunAsync(
            static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root));
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var annex = await EuImageOnlyAnnexProducerTests.ProduceForEnvelopeAsync();
        var envelope = Rebuild(
            AddEuropeCorpusRecord(europe, annex.Disposition!.SourceObject),
            luxembourg,
            [annex]);

        var lineage = Stage3EvidenceLineage.TryBind(envelope, out var refusal, out var detail);

        Assert.AreEqual(Stage3EvidenceLineageRefusal.None, refusal, detail);
        Assert.IsNotNull(lineage);
    }

    private static async Task<Stage3EvidenceEnvelope> CompleteEnvelopeAsync()
    {
        var europe = await EuAxiomWiringHarness.RunAsync(
            static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root));
        var luxembourg = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        return Rebuild(europe, luxembourg);
    }

    private static Stage3EvidenceEnvelope Rebuild(
        EuQueryExecutionResult europe,
        LuxembourgQueryExecutionResult luxembourg,
        IEnumerable<EuImageOnlyAnnexProductionResult>? annexes = null)
    {
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(europe);
        return Stage3EvidenceEnvelope.TryCreate(
            europe,
            luxembourg,
            formex,
            annexes ?? [],
            out var refusal,
            out var detail)
            ?? throw new AssertFailedException($"Envelope refused: {refusal}: {detail}");
    }

    private static EuQueryExecutionResult CopyEurope(
        EuQueryExecutionResult source,
        SourceArtifactRef? corpusRecordSetRef = null,
        VerifiedCorpusRecordSet? corpusRecordSet = null,
        string? manifestCanonicalSha256 = null,
        DurableBlobWriteReceipt? scopeManifestReceipt = null)
    {
        var corpus = corpusRecordSet ?? source.CorpusRecordSet!;
        var recordSetResult = CorpusRecordSetWriteResult.Written(
            corpusRecordSetRef ?? source.CorpusRecordSetRef!,
            corpus,
            Complete(corpus),
            CustodyMembership.Floored);
        return EuQueryExecutionResult.DeliveredWithLocatedAmendments(
            source.Topology,
            source.FamilyOutcomes,
            source.ObservedObjectCount,
            source.ObservedExpressionCount,
            source.ReductionExclusions,
            source.WatermarkWitnessPlan!,
            source.RootBinding!,
            source.WitnessReconciliation!,
            source.WitnessTerminations!,
            scopeManifestReceipt ?? source.ScopeManifestReceipt!,
            manifestCanonicalSha256 ?? source.ScopeManifestCanonicalSha256!,
            source.DocumentAcquisitionOutcomesByOrdinal!,
            source.DocumentLadderResultsByOrdinal!,
            source.ObservedManifestationTypesByCelex!,
            source.ObservedExpressionsByCelex!,
            source.MintedRowsByOrdinal!,
            source.DateAxioms,
            source.LocatedAmendmentObservations,
            recordSetResult,
            source.CorrigendumTripwires!);
    }

    internal static EuQueryExecutionResult AddEuropeCorpusRecord(
        EuQueryExecutionResult source,
        SourceObjectRef objectRef)
    {
        var set = source.CorpusRecordSet!.Set;
        var template = set.Records[^1];
        var record = new CorpusRecord(
            CorpusRecordSchemaIds.Record,
            objectRef,
            template.ObjectOrdinal + 1,
            template.RecordDisposition,
            template.BodyDisposition,
            template.RelationDisposition,
            template.SupportingDocumentDisposition,
            template.Body,
            set.ManifestRef,
            set.RunIdentity);
        var rebuilt = new CorpusRecordSet(
            CorpusRecordSetSchemaIds.Set,
            set.ManifestRef,
            set.RunIdentity,
            [.. set.Records, record]);
        using var bytes = new MemoryStream();
        var sha256 = CorpusRecordSetCanonicalWriter.Write(bytes, rebuilt);
        var reference = new SourceArtifactRef(source.CorpusRecordSetRef!.ResourceId, sha256);
        var verified = VerifiedCorpusRecordSet.ParseAndVerify(reference, bytes.ToArray());
        return CopyEurope(source, reference, verified);
    }

    private static LuxembourgQueryExecutionResult CopyLuxembourg(
        LuxembourgQueryExecutionResult source,
        SourceArtifactRef? corpusRecordSetRef = null,
        string? manifestCanonicalSha256 = null,
        DurableBlobWriteReceipt? scopeManifestReceipt = null) =>
        LuxembourgQueryExecutionResult.Delivered(
            source.Topology,
            source.FamilyOutcomes,
            source.RelationFamilyAcquisitions,
            source.ResolvedRelations,
            source.LocalInboundRelations,
            source.TypedAssertions,
            source.ResourceObservationSubjects,
            source.ResourceObservationExclusions,
            scopeManifestReceipt ?? source.ScopeManifestReceipt!,
            manifestCanonicalSha256 ?? source.ScopeManifestCanonicalSha256!,
            source.DocumentAcquisitionOutcomesByOrdinal!,
            corpusRecordSetRef ?? source.CorpusRecordSetRef!,
            source.CorpusRecordSet!,
            source.GazetteBodySetsByOrdinal!,
            source.GazetteListingFetchRefusalsByOrdinal!,
            source.GazetteListingsWithContradictoryLegalValueByOrdinal!,
            source.PopulationLedger!);

    private static CorpusRecordSetCompletion Complete(VerifiedCorpusRecordSet corpus)
    {
        var entries = corpus.Set.Records.Select(record => new CorpusRecordOutcomeEntry(
            record.ObjectRef,
            record.ObjectOrdinal,
            record.Body.Kind switch
            {
                CorpusBodyRecordKind.Held => CorpusRecordOutcomeKind.Held,
                CorpusBodyRecordKind.NotHeld => CorpusRecordOutcomeKind.NotHeld,
                _ => CorpusRecordOutcomeKind.PendingAcquisition,
            },
            record.Body.NotHeldReason,
            record.Body.PendingAcquisitionReason?.Kind,
            record.Body.PendingAcquisitionReason?.Refusal)).ToArray();
        return new CorpusRecordSetCompletion(
            CorpusRecordSetCompletionState.Complete,
            entries.Length,
            entries);
    }
}
