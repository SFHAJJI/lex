using System.Reflection;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Corpus;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Ingest.Europe;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LuxembourgPdfProfileEligibilityProducerTests
{
    [TestMethod]
    public async Task ExactGazetteEvidenceClassifiesTheSelectedPdfWithoutInterpretingIt()
    {
        var composition = await CompleteCompositionAsync();

        var population = LuxembourgPdfProfileEligibilityProducer.Produce(composition);

        Assert.AreSame(composition, population.SourceComposition);
        Assert.HasCount(composition.LuxembourgDerivationPopulation.Inputs.Count, population.Outcomes);
        var outcome = population.Outcomes.Single();
        Assert.AreEqual(LuxembourgPdfProfileEligibilityDisposition.GazettePdfEligible, outcome.Disposition);
        Assert.IsNull(outcome.GapReason);
        Assert.IsNotNull(outcome.GazetteEvidence);
        Assert.AreEqual(outcome.Input.SelectedWemiCandidate.ExpressionIri, outcome.PublisherExpressionIri);
        Assert.AreEqual(outcome.Input.SelectedWemiCandidate.ManifestationIri, outcome.PublisherManifestationIri);
        Assert.AreEqual(outcome.Input.SelectedWemiCandidate.ItemIri, outcome.PublisherItemIri);
        Assert.AreSame(outcome.Input.Receipt, outcome.TransportReceipt);
        Assert.AreEqual(
            outcome.Input.Receipt,
            outcome.GazetteEvidence.RetainedTransportBytes,
            "eligibility is bound to the exact retained receipt already admitted by the Gazette producer");
    }

    [TestMethod]
    public async Task EqualPublisherFactsHaveStableEligibilityIdentityAcrossExecutions()
    {
        var first = LuxembourgPdfProfileEligibilityProducer.Produce(await CompleteCompositionAsync());
        var second = LuxembourgPdfProfileEligibilityProducer.Produce(await CompleteCompositionAsync());

        Assert.AreEqual(first.IdentitySha256, second.IdentitySha256);
        Assert.AreEqual(
            first.Outcomes.Single().SemanticIdentitySha256,
            second.Outcomes.Single().SemanticIdentitySha256);
    }

    [TestMethod]
    public void ThePublicDoorAcceptsOnlyTheProofBoundComposition()
    {
        var parameters = typeof(LuxembourgPdfProfileEligibilityProducer)
            .GetMethod(nameof(LuxembourgPdfProfileEligibilityProducer.Produce))!
            .GetParameters()
            .Select(static parameter => parameter.ParameterType)
            .ToArray();

        CollectionAssert.AreEqual(new[] { typeof(Stage3BodyComposition) }, parameters);
    }

    [TestMethod]
    public async Task EveryDispositionAndTypedGapIsExplicit()
    {
        var composition = await CompleteCompositionAsync();
        var input = composition.LuxembourgDerivationPopulation.Inputs.Single();
        var gazette = composition.Luxembourg.SelectMany(static value => value.GazetteBodies.Bodies).Single();

        AssertOutcome(
            Classify(XmlInput(input), []),
            LuxembourgPdfProfileEligibilityDisposition.NotPdf);
        AssertOutcome(
            Classify(input, []),
            LuxembourgPdfProfileEligibilityDisposition.PublisherPdfEligible);
        AssertOutcome(
            Classify(input, [gazette, gazette]),
            LuxembourgPdfProfileEligibilityDisposition.TypedGap,
            LuxembourgPdfProfileEligibilityGapReason.GazetteEvidenceAmbiguous);
        AssertOutcome(
            Classify(input, [LuxembourgGazetteBodyDisposition.Create(gazette.Candidate, null)]),
            LuxembourgPdfProfileEligibilityDisposition.TypedGap,
            LuxembourgPdfProfileEligibilityGapReason.GazetteEvidenceNotAdmitted);
        AssertOutcome(
            Classify(InputWith(input, receipt: OtherReceipt()), [gazette]),
            LuxembourgPdfProfileEligibilityDisposition.TypedGap,
            LuxembourgPdfProfileEligibilityGapReason.GazetteReceiptMismatch);
    }

    [TestMethod]
    public async Task PopulationOrdersEveryHeldInputAndEmitsExactlyOneOutcome()
    {
        var original = await CompleteCompositionAsync();
        var input = original.LuxembourgDerivationPopulation.Inputs.Single();
        var laterRecord = CopyRecord(input.CorpusRecord, input.ObjectOrdinal + 1);
        var earlierRecord = CopyRecord(input.CorpusRecord, input.ObjectOrdinal - 1);
        var source = CompositionWithInputs(
            original,
            [InputWith(input, record: laterRecord), InputWith(input, record: earlierRecord)],
            includeGazetteBodies: false);

        var population = LuxembourgPdfProfileEligibilityProducer.Produce(source);

        CollectionAssert.AreEqual(
            new[] { earlierRecord.ObjectOrdinal, laterRecord.ObjectOrdinal },
            population.Outcomes.Select(static outcome => outcome.Input.ObjectOrdinal).ToArray());
        Assert.HasCount(2, population.Outcomes);
        Assert.IsTrue(population.Outcomes.All(static outcome =>
            outcome.Disposition == LuxembourgPdfProfileEligibilityDisposition.PublisherPdfEligible));
    }

    private static LuxembourgPdfProfileEligibilityOutcome Classify(
        LuxembourgHeldBodyDerivationInput input,
        IReadOnlyList<LuxembourgGazetteBodyDisposition> gazetteBodies) =>
        (LuxembourgPdfProfileEligibilityOutcome)typeof(LuxembourgPdfProfileEligibilityProducer)
            .GetMethod("Classify", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [input, gazetteBodies])!;

    private static void AssertOutcome(
        LuxembourgPdfProfileEligibilityOutcome outcome,
        LuxembourgPdfProfileEligibilityDisposition disposition,
        LuxembourgPdfProfileEligibilityGapReason? gapReason = null)
    {
        Assert.AreEqual(disposition, outcome.Disposition);
        Assert.AreEqual(gapReason, outcome.GapReason);
        Assert.AreEqual(
            disposition == LuxembourgPdfProfileEligibilityDisposition.GazettePdfEligible,
            outcome.GazetteEvidence is not null);
    }

    private static LuxembourgHeldBodyDerivationInput XmlInput(
        LuxembourgHeldBodyDerivationInput source)
    {
        var candidate = source.SelectedWemiCandidate;
        var item = candidate.ItemIri[..candidate.ItemIri.LastIndexOf('.')] + ".xml";
        var manifestation = candidate.ManifestationIri[..candidate.ManifestationIri.LastIndexOf('/')] + "/xml";
        var format = candidate.FormatIri[..(candidate.FormatIri.LastIndexOf('/') + 1)] + "xml";
        var xml = new LuxembourgWemiCandidate(
            candidate.RootIri,
            candidate.ExpressionIri,
            manifestation,
            item,
            candidate.LanguageIri,
            format,
            candidate.ObservationRef,
            LuxembourgWemiCandidateDisposition.StructurallyConsistent,
            []);
        var address = LuxembourgDocumentFetchAddress.Create(
            LuxembourgFileUri.RequireValid(item),
            LuxembourgUserFormatToken.Xml,
            source.Address.LegalValue,
            source.Address.ActEliPagePath);
        return new LuxembourgHeldBodyDerivationInput(
            source.CorpusRecord,
            new LuxembourgSelectedDocumentFetch(address, xml),
            source.Receipt);
    }

    private static LuxembourgHeldBodyDerivationInput InputWith(
        LuxembourgHeldBodyDerivationInput source,
        CorpusRecord? record = null,
        DurableBlobWriteReceipt? receipt = null) =>
        new(
            record ?? source.CorpusRecord,
            new LuxembourgSelectedDocumentFetch(source.Address, source.SelectedWemiCandidate),
            receipt ?? source.Receipt);

    private static DurableBlobWriteReceipt OtherReceipt()
    {
        var reference = new DurableBlobRef(
            CustodySchemaIds.DurableBlobRef,
            new string('f', 64),
            1,
            CustodyClass.NightlyFloor90d);
        var policy = new CustodyPolicyEvidence(
            CustodySchemaIds.CustodyPolicyEvidence,
            reference,
            CustodyVerificationProfile.FileSystemUnenforced1,
            null,
            CustodyProtection.NotEnforced,
            new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero),
            null);
        return new DurableBlobWriteReceipt(CustodySchemaIds.DurableBlobWriteReceipt, reference, policy);
    }

    private static CorpusRecord CopyRecord(CorpusRecord source, int ordinal) =>
        new(
            source.Schema,
            source.ObjectRef,
            ordinal,
            source.RecordDisposition,
            source.BodyDisposition,
            source.RelationDisposition,
            source.SupportingDocumentDisposition,
            source.Body,
            source.ManifestRef,
            source.RunIdentity);

    private static Stage3BodyComposition CompositionWithInputs(
        Stage3BodyComposition source,
        IReadOnlyList<LuxembourgHeldBodyDerivationInput> inputs,
        bool includeGazetteBodies)
    {
        var population = (LuxembourgHeldBodyDerivationPopulation)typeof(LuxembourgHeldBodyDerivationPopulation)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single()
            .Invoke([source.LuxembourgDerivationPopulation.CorpusRecordSet, inputs]);
        return (Stage3BodyComposition)typeof(Stage3BodyComposition)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single()
            .Invoke([
                source.Envelope,
                source.Europe,
                includeGazetteBodies ? source.Luxembourg : Array.Empty<Stage3LuxembourgBodyComposition>(),
                population,
            ]);
    }

    private static async Task<Stage3BodyComposition> CompleteCompositionAsync()
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
        var envelope = Stage3EvidenceEnvelope.TryCreate(
            europe,
            luxembourg,
            formex,
            classifications,
            fidelity,
            Stage3EvidenceEnvelopeTests.CompleteAknInventory(luxembourg),
            out var envelopeRefusal,
            out var envelopeDetail);
        Assert.IsNotNull(envelope, $"{envelopeRefusal}: {envelopeDetail}");
        var composition = Stage3BodyComposition.TryCreate(envelope, out var refusal, out var detail);
        Assert.IsNotNull(composition, $"{refusal}: {detail}");
        return composition;
    }
}
