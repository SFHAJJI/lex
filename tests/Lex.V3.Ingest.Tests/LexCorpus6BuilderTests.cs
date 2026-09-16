namespace Lex.V3.Ingest.Tests;

[TestClass]
public sealed class LexCorpus6BuilderTests
{
    [TestMethod]
    public void TerminalBuilderAndStrictReaderAreOneVerticalSlice()
    {
        var schema = (string)typeof(LexCorpus6Builder)
            .GetField(nameof(LexCorpus6Builder.Schema))!
            .GetRawConstantValue()!;
        Assert.AreEqual("lex-corpus/6", schema);
        Assert.IsNotNull(typeof(LexCorpus6Builder).GetMethod(nameof(LexCorpus6Builder.TryBuild)));
        Assert.IsNotNull(typeof(VerifiedLexCorpus6ManifestSet).GetMethod(
            nameof(VerifiedLexCorpus6ManifestSet.ParseAndVerify)));
    }

    [TestMethod]
    public async Task ProductionAdaptersRetainExactRightsInputsForTheTerminalBuilder()
    {
        var europe = await EuAxiomWiringHarness.RunAsync(
            static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root));
        Assert.IsNull(europe.Refusal);
        Assert.IsNotNull(europe.HeldBodyContentClasses);
        var heldEurope = europe.CorpusRecordSet!.Set.Records
            .Where(static record =>
                record.Body.Kind == Lex.V3.Contracts.Source.Corpus.CorpusBodyRecordKind.Held)
            .ToArray();
        Assert.HasCount(heldEurope.Length, europe.HeldBodyContentClasses);
        foreach (var record in heldEurope)
        {
            Assert.IsTrue(europe.HeldBodyContentClasses.ContainsKey(record.ObjectRef));
        }

        var pdfBytes = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "LuDocumentFetch",
            "lu-pdf-consolidated-2020-04-08-a265.bin"));
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync(pdfBytes);
        Assert.IsNull(luxembourg.Refusal);
        Assert.IsNotNull(luxembourg.HeldBodyDerivationPopulation);
        Assert.IsTrue(luxembourg.HeldBodyDerivationPopulation.Inputs.Count > 0);
        Assert.IsTrue(luxembourg.HeldBodyDerivationPopulation.Inputs.All(
            static input => input.RightsResolution is not null));
    }

    [TestMethod]
    public async Task CompleteProductionEvidenceBuildsOneDeterministicStrictlyReopenableMemberPerSourceUnit()
    {
        var envelope = await CompleteProfileEnvelopeAsync();
        var matrix = CompleteEuropeRightsMatrix();

        var first = LexCorpus6Builder.TryBuild(envelope, matrix, out var firstRefusal, out var firstDetail);
        var second = LexCorpus6Builder.TryBuild(envelope, matrix, out var secondRefusal, out var secondDetail);

        Assert.IsNotNull(first, $"{firstRefusal}: {firstDetail}");
        Assert.IsNotNull(second, $"{secondRefusal}: {secondDetail}");
        CollectionAssert.AreEqual(first.CanonicalBytes.ToArray(), second.CanonicalBytes.ToArray());
        Assert.AreEqual(first.ArtifactRef, second.ArtifactRef);
        var expected = envelope.BodyComposition.Envelope.Europe.CorpusRecordSet!.Set.Records.Count
            + envelope.BodyComposition.Envelope.Luxembourg.CorpusRecordSet!.Set.Records.Count;
        Assert.HasCount(expected, first.VerifiedSet.Set.Members);
        Assert.HasCount(
            expected,
            first.VerifiedSet.Set.Members
                .Select(static member => (member.Publisher, member.ObjectRefSha256))
                .Distinct()
                .ToArray());
        Assert.IsTrue(first.VerifiedSet.Set.Members.Any(static member =>
            member.Outcome == LexCorpus6OutcomeKind.RightsWithheld &&
            member.BodySha256 is not null &&
            member.BodyReceiptSha256 is not null));
        var reopened = VerifiedLexCorpus6ManifestSet.ParseAndVerify(
            first.ArtifactRef,
            first.CanonicalBytes.Span);
        Assert.HasCount(first.VerifiedSet.Set.Members.Count, reopened.Set.Members);
        CollectionAssert.AreEqual(
            first.VerifiedSet.Set.Members.Select(static member => member.ObjectRefSha256).ToArray(),
            reopened.Set.Members.Select(static member => member.ObjectRefSha256).ToArray());
    }

    private static async Task<Stage3DerivationProfileEnvelope> CompleteProfileEnvelopeAsync()
    {
        var europe = await EuAxiomWiringHarness.RunAsync(
            static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root));
        var bytes = File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "LuDocumentFetch",
            "lu-pdf-consolidated-2020-04-08-a265.bin"));
        var luxembourg = await LuxembourgGazetteAcquisitionTests
            .CompletePublisherPdfForStage3BodyCompositionAsync(bytes);
        var formex = EuFormexRunOutcomeReconciliationTests.CompleteForEnvelope(europe);
        var classifications = Stage3EvidenceEnvelopeTests.CompleteClassifications(formex);
        var fidelity = Stage3FidelityPreservationReconciliationTests.Complete(europe, luxembourg);
        var evidence = Stage3EvidenceEnvelopeTests.TryCreate(
            europe,
            luxembourg,
            formex,
            classifications,
            fidelity,
            out var evidenceRefusal,
            out var evidenceDetail);
        Assert.IsNotNull(evidence, $"{evidenceRefusal}: {evidenceDetail}");
        var composition = Stage3BodyComposition.TryCreate(
            evidence,
            out var compositionRefusal,
            out var compositionDetail);
        Assert.IsNotNull(composition, $"{compositionRefusal}: {compositionDetail}");
        var eligibility = Luxembourg.LuxembourgPdfProfileEligibilityProducer.Produce(composition);
        Lex.V3.Contracts.Custody.ICustodyStore store =
            new RoutedHttpAcquisitionSessionTests.MultiObjectCustodyStore();
        _ = await store.CreateAsync(bytes, Lex.V3.Contracts.Custody.CustodyClass.NightlyFloor90d, CancellationToken.None);
        var layout = await new Luxembourg.LuxembourgPdfLayoutEvidenceProducer(store)
            .RunAsync(eligibility, CancellationToken.None);
        Assert.IsTrue(layout.Produced, $"{layout.Refusal}: {layout.Detail}");
        var text = await new Luxembourg.LuxembourgPublisherPdfTextLayerProfileProducer(store)
            .RunAsync(layout.Population!, CancellationToken.None);
        Assert.IsTrue(text.Produced, $"{text.Refusal}: {text.Detail}");
        var actScope = Luxembourg.LuxembourgPublisherPdfActScopeProducer.Produce(text.Population!);
        var envelope = Stage3DerivationProfileEnvelope.TryCreate(
            actScope,
            out var profileRefusal,
            out var profileDetail);
        Assert.IsNotNull(envelope, $"{profileRefusal}: {profileDetail}");
        return envelope;
    }

    private static Lex.V3.Contracts.Source.Europe.EuRightsMatrix CompleteEuropeRightsMatrix()
    {
        var evidence = new Lex.V3.Contracts.Source.Core.SourceArtifactRef(
            "urn:uuid:11111111-1111-1111-1111-111111111111",
            new string('a', 64));
        var classes = Enum.GetValues<Lex.V3.Contracts.Source.Europe.EuContentClass>()
            .Select(value => new Lex.V3.Contracts.Source.Europe.EuRightsDisposition(
                value,
                Lex.V3.Contracts.Source.Europe.EuRightsDisposition.BasisFor(value),
                evidence))
            .ToArray();
        var channels = Enum.GetValues<Lex.V3.Contracts.Source.Europe.EuRightsExceptionChannel>()
            .Select(value => new Lex.V3.Contracts.Source.Europe.EuRightsExceptionDisposition(value, evidence))
            .ToArray();
        var matrix = Lex.V3.Contracts.Source.Europe.EuRightsMatrix.TryAdmit(
            classes,
            channels,
            out var refusal);
        Assert.IsNotNull(matrix, refusal.ToString());
        return matrix;
    }
}
