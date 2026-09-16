using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class Stage3BodyCompositionTests
{
    [TestMethod]
    public async Task TheEnvelopeComposesCustodyAndBodyOutcomesWithoutFlatteningThem()
    {
        var acquired = await EuFormexAnnexClassificationReconciliationTests.AcquiredFixtureAsync();
        var europe = new[]
            {
                acquired.Classification.Binding.FormexSource.ObjectRef,
                acquired.Classification.Binding.XhtmlSource.ObjectRef,
                acquired.Classification.Binding.PdfSource.ObjectRef,
            }
            .Aggregate(acquired.Run, Stage3EvidenceLineageTests.AddEuropeCorpusRecord);
        var luxembourg = await LuxembourgGazetteAcquisitionTests.CompleteForStage3BodyCompositionAsync();
        var formex = EuFormexAnnexClassificationReconciliationTests.Reconciliation(
            europe, [acquired.Outcome]);
        var classifications = Stage3EvidenceEnvelopeTests.CompleteClassifications(
            formex, [acquired.Classification]);
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(europe, luxembourg);
        var envelope = Stage3EvidenceEnvelopeTests.TryCreate(
            europe, luxembourg, formex, classifications, fidelity, out var envelopeRefusal, out var envelopeDetail);
        Assert.IsNotNull(envelope, $"{envelopeRefusal}: {envelopeDetail}");

        var composition = Stage3BodyComposition.TryCreate(
            envelope, out var refusal, out var detail);

        Assert.AreEqual(Stage3BodyCompositionRefusal.None, refusal, detail);
        Assert.IsNotNull(composition);
        Assert.AreSame(envelope, composition.Envelope);
        var eu = composition.Europe.Single();
        Assert.AreSame(acquired.Classification, eu.Classification);
        CollectionAssert.AreEqual(
            acquired.Classification.Members.ToArray(),
            eu.Annexes.ToArray(),
            "The exact ordered (outcome?, gap) members remain authoritative; no general admission is synthesized.");
        Assert.IsTrue(eu.Annexes.All(static annex => annex.Outcome is null));
        Assert.IsTrue(eu.Annexes.All(static annex =>
            annex.Gap == EuBoundAnnexBodyClassificationGap.MappingUnresolved));
        Assert.AreSame(acquired.Classification.Binding.FormexSource, eu.FormexCustody);
        Assert.AreSame(acquired.Classification.Binding.XhtmlSource, eu.XhtmlCustody);
        Assert.AreSame(acquired.Classification.Binding.PdfSource, eu.PdfCustody);

        var lu = composition.Luxembourg.Single();
        var (ordinal, bodySet) = luxembourg.GazetteBodySetsByOrdinal!.Single();
        Assert.AreEqual(ordinal, lu.Custody.ObjectOrdinal);
        Assert.AreSame(bodySet, lu.GazetteBodies);
        Assert.AreEqual(bodySet.PublisherActIri, lu.Custody.ObjectRef.PublisherUri);
        Assert.AreSame(
            luxembourg.HeldBodyDerivationPopulation,
            composition.LuxembourgDerivationPopulation);
    }

    [TestMethod]
    public async Task ABodySetOutsideTheProofBoundCorpusIsRefused()
    {
        var europe = await EuAxiomWiringHarness.RunAsync(
            static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root));
        var original = await LuxembourgGazetteAcquisitionTests.CompleteForStage3BodyCompositionAsync();
        var bodySet = original.GazetteBodySetsByOrdinal!.Values.Single();
        var luxembourg = CopyLuxembourg(original, new Dictionary<int, Contracts.Source.Luxembourg.LuxembourgGazetteBodySet>
        {
            [int.MaxValue] = bodySet,
        });
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(europe);
        var classifications = Stage3EvidenceEnvelopeTests.CompleteClassifications(formex);
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(europe, luxembourg);
        var envelope = Stage3EvidenceEnvelopeTests.TryCreate(
            europe, luxembourg, formex, classifications, fidelity, out var envelopeRefusal, out var envelopeDetail);
        Assert.IsNotNull(envelope, $"{envelopeRefusal}: {envelopeDetail}");

        Assert.IsNull(Stage3BodyComposition.TryCreate(envelope, out var refusal, out var detail));
        Assert.AreEqual(Stage3BodyCompositionRefusal.LuxembourgBodySetOutsideCorpus, refusal);
        Assert.AreEqual(int.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), detail);
    }

    [TestMethod]
    public async Task ABodySetCannotBeAttachedToAnotherCorpusObject()
    {
        var europe = await EuAxiomWiringHarness.RunAsync(
            static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root));
        var original = await LuxembourgGazetteAcquisitionTests.CompleteForStage3BodyCompositionAsync();
        var bodySet = original.GazetteBodySetsByOrdinal!.Values.Single();
        var foreign = original.CorpusRecordSet!.Set.Records.First(record =>
            !string.Equals(record.ObjectRef.PublisherUri, bodySet.PublisherActIri, StringComparison.Ordinal));
        var luxembourg = CopyLuxembourg(original, new Dictionary<int, Contracts.Source.Luxembourg.LuxembourgGazetteBodySet>
        {
            [foreign.ObjectOrdinal] = bodySet,
        });
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(europe);
        var classifications = Stage3EvidenceEnvelopeTests.CompleteClassifications(formex);
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(europe, luxembourg);
        var envelope = Stage3EvidenceEnvelopeTests.TryCreate(
            europe, luxembourg, formex, classifications, fidelity, out var envelopeRefusal, out var envelopeDetail);
        Assert.IsNotNull(envelope, $"{envelopeRefusal}: {envelopeDetail}");

        Assert.IsNull(Stage3BodyComposition.TryCreate(envelope, out var refusal, out var detail));
        Assert.AreEqual(Stage3BodyCompositionRefusal.LuxembourgBodySetObjectMismatch, refusal);
        Assert.AreEqual(bodySet.PublisherActIri, detail);
    }

    [TestMethod]
    public void ThePublicDoorAcceptsOnlyTheProofBoundEnvelope()
    {
        var parameters = typeof(Stage3BodyComposition)
            .GetMethod(nameof(Stage3BodyComposition.TryCreate))!
            .GetParameters()
            .Select(static parameter => parameter.ParameterType)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                typeof(Stage3EvidenceEnvelope),
                typeof(Stage3BodyCompositionRefusal).MakeByRefType(),
                typeof(string).MakeByRefType(),
            },
            parameters);
    }

    private static LuxembourgQueryExecutionResult CopyLuxembourg(
        LuxembourgQueryExecutionResult source,
        IReadOnlyDictionary<int, Contracts.Source.Luxembourg.LuxembourgGazetteBodySet> bodySets) =>
        LuxembourgQueryExecutionResult.Delivered(
            source.Topology,
            source.FamilyOutcomes,
            source.RelationFamilyAcquisitions,
            source.ResolvedRelations,
            source.LocalInboundRelations,
            source.TypedAssertions,
            source.ResourceObservationSubjects,
            source.ResourceObservationExclusions,
            source.ScopeManifestReceipt!,
            source.ScopeManifestCanonicalSha256!,
            source.DocumentAcquisitionOutcomesByOrdinal!,
            source.CorpusRecordSetRef!,
            source.CorpusRecordSetReceipt!,
            source.CorpusRecordSet!,
            source.ObservedObjectIdentitySetRef!,
            source.ObservedObjectIdentitySetReceipt!,
            source.ObservedObjectIdentitySet!,
            source.HeldBodyDerivationPopulation!,
            bodySets,
            source.GazetteListingFetchRefusalsByOrdinal!,
            source.GazetteListingsWithContradictoryLegalValueByOrdinal!,
            source.PopulationLedger!);
}
