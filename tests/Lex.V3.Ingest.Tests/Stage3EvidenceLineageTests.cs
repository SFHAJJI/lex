using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Derivation;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Corpus;
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
    public async Task LuxembourgDerivationPopulationMustBelongToTheExactCorpusSet()
    {
        var original = await CompleteEnvelopeAsync();
        var foreign = await LuxembourgQueryExecutionAdapterTests.RunEmptyDeliveredForEnvelopeAsync();
        var substituted = CopyLuxembourg(
            original.Luxembourg,
            heldBodyDerivationPopulation: foreign.HeldBodyDerivationPopulation);
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(original.Europe);
        var classifications = Stage3EvidenceEnvelopeTests.CompleteClassifications(formex);
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(
            original.Europe, substituted);

        var envelope = Stage3EvidenceEnvelopeTests.TryCreate(
            original.Europe,
            substituted,
            formex,
            classifications,
            fidelity,
            out var refusal,
            out var detail);

        Assert.IsNull(envelope);
        Assert.AreEqual(
            Stage3EvidenceEnvelopeRefusal.LuxembourgDerivationPopulationMismatch,
            refusal,
            detail);
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
        LuxembourgQueryExecutionResult luxembourg)
    {
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(europe);
        var classifications = Stage3EvidenceEnvelopeTests.CompleteClassifications(formex);
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(europe, luxembourg);
        return Stage3EvidenceEnvelopeTests.TryCreate(
            europe,
            luxembourg,
            formex,
            classifications,
            fidelity,
            out var refusal,
            out var detail)
            ?? throw new AssertFailedException($"Envelope refused: {refusal}: {detail}");
    }

    /// <summary>
    /// The premise a Luxembourg run's scope reduction was admitted against must be that run's own.
    /// A complete, well-formed identity set belonging to a different execution is refused at the
    /// terminal lineage door.
    /// </summary>
    /// <remarks>
    /// FOUND IN REVIEW. The artifact carried a RunIdentity from the start, and nothing read it:
    /// the reader had no expected-run input, <c>Delivered</c> only null-checked the new reference
    /// and receipt, and this door ignored both fields. A foreign set therefore reached Stage 3
    /// lineage untouched. The substituted set here is REAL -- same shape, same canonical form, its
    /// own correct reference -- so what refuses it is whose it is, not that it fails to parse.
    /// </remarks>
    [TestMethod]
    public async Task LuxembourgObservedIdentitySetCannotBeSubstituted()
    {
        var envelope = await CompleteEnvelopeAsync();

        var foreign = LuxembourgObservedObjectIdentitySetTests.BuildFor(
            new SourceArtifactRef(
                "urn:uuid:88888888-8888-4888-8888-888888888888", new string('8', 64)));

        var substituted = Rebuild(
            envelope.Europe,
            CopyLuxembourg(
                envelope.Luxembourg,
                observedObjectIdentitySetRef: foreign.Reference,
                observedObjectIdentitySet: foreign.Set));

        var lineage = Stage3EvidenceLineage.TryBind(substituted, out var refusal, out var detail);

        Assert.IsNull(lineage, "a set from another run must not bind into Stage 3 lineage.");
        Assert.AreEqual(
            Stage3EvidenceLineageRefusal.LuxembourgObservedIdentitySetRunMismatch, refusal, detail);
    }

    /// <summary>
    /// And the reference has to be the digest of the set beside it: a reference and bytes that
    /// disagree name something no later party would reopen.
    /// </summary>
    [TestMethod]
    public async Task LuxembourgObservedIdentitySetReferenceMustBeItsOwnDigest()
    {
        var envelope = await CompleteEnvelopeAsync();

        var substituted = Rebuild(
            envelope.Europe,
            CopyLuxembourg(
                envelope.Luxembourg,
                observedObjectIdentitySetRef: new SourceArtifactRef(
                    envelope.Luxembourg.ObservedObjectIdentitySetRef!.ResourceId,
                    new string('9', 64))));

        var lineage = Stage3EvidenceLineage.TryBind(substituted, out var refusal, out var detail);

        Assert.IsNull(lineage);
        Assert.AreEqual(
            Stage3EvidenceLineageRefusal.LuxembourgObservedIdentitySetMismatch, refusal, detail);
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
            RetainedSetReceipt(),
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
        var template = source.CorpusRecordSet!.Set.Records[^1];
        return AddEuropeCorpusRecord(source, objectRef, template.Body);
    }

    internal static EuQueryExecutionResult AddEuropeHeldCorpusRecord(
        EuQueryExecutionResult source,
        SourceObjectRef objectRef,
        DurableBlobWriteReceipt receipt) =>
        AddEuropeCorpusRecord(source, objectRef, CorpusBodyRecord.Held(receipt));

    private static EuQueryExecutionResult AddEuropeCorpusRecord(
        EuQueryExecutionResult source,
        SourceObjectRef objectRef,
        CorpusBodyRecord body)
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
            body,
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
        DurableBlobWriteReceipt? scopeManifestReceipt = null,
        SourceArtifactRef? observedObjectIdentitySetRef = null,
        VerifiedLuxembourgObservedObjectIdentitySet? observedObjectIdentitySet = null,
        LuxembourgHeldBodyDerivationPopulation? heldBodyDerivationPopulation = null) =>
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
            source.CorpusRecordSetReceipt!,
            source.CorpusRecordSet!,
            observedObjectIdentitySetRef ?? source.ObservedObjectIdentitySetRef!,
            source.ObservedObjectIdentitySetReceipt!,
            observedObjectIdentitySet ?? source.ObservedObjectIdentitySet!,
            heldBodyDerivationPopulation ?? source.HeldBodyDerivationPopulation!,
            source.GazetteBodySetsByOrdinal!,
            source.GazetteListingFetchRefusalsByOrdinal!,
            source.GazetteListingsWithContradictoryLegalValueByOrdinal!,
            source.PopulationLedger!);

    /// <summary>
    /// A stand-in custody address for a set these fixtures fabricate rather than write. The lineage
    /// under test never reads it; it exists because a written result now has to name the address its
    /// own bytes were retained at, and a fixture that fabricates the result fabricates that too.
    /// </summary>
    private static DurableBlobWriteReceipt RetainedSetReceipt()
    {
        var observedAt = new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero);
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef, new string('d', 64), 2048, CustodyClass.NightlyFloor90d);
        var policy = new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            reference,
            CustodyVerificationProfile.ImmutableObject1,
            Guid.Parse("00000000-0000-0000-0000-0000000000d1"),
            CustodyProtection.LockedTime,
            observedAt,
            observedAt.AddDays(91));
        return new DurableBlobWriteReceipt(CustodySchemaIds.DurableBlobWriteReceipt, reference, policy);
    }

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
