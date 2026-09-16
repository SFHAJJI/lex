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
        Assert.IsTrue(luxembourg.HeldBodyDerivationPopulation.Inputs.All(static input =>
            string.Equals(
                input.RightsResolution!.SelectedManifestationIri,
                input.SelectedWemiCandidate.ManifestationIri,
                StringComparison.Ordinal) &&
            input.RightsResolution.BoundRunIdentity == input.RightsResolution.SparqlObservations.RunIdentity &&
            input.RightsResolution.BoundRunIdentity == input.RightsResolution.InFileObservations.RunIdentity));
        Assert.IsTrue(luxembourg.HeldBodyDerivationPopulation.Inputs.Any(static input =>
            input.RightsResolution!.BoundRunIdentity != input.CorpusRecord.RunIdentity),
            "The final rights observation run and the corpus acquisition run are distinct provenance domains.");
    }

    [TestMethod]
    public async Task CompleteProductionEvidenceBuildsOneDeterministicStrictlyReopenableMemberPerSourceUnit()
    {
        var envelope = await CompleteProfileEnvelopeAsync();
        var matrix = CompleteEuropeRightsMatrix(envelope.BodyComposition.Envelope.EuropeLegalNoticeEvidence!);

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
        Assert.HasCount(
            Enum.GetValues<Lex.V3.Contracts.Source.Europe.EuContentClass>().Length,
            first.VerifiedSet.Set.EuropeRightsMatrix.ContentClasses);
        Assert.HasCount(
            Enum.GetValues<Lex.V3.Contracts.Source.Europe.EuRightsExceptionChannel>().Length,
            first.VerifiedSet.Set.EuropeRightsMatrix.ExceptionChannels);
        Assert.IsTrue(first.VerifiedSet.Set.Members
            .Where(static member =>
                member.Publisher == Lex.V3.Contracts.PublisherId.EuEurLex &&
                member.BodySha256 is not null)
            .All(static member =>
                member.EuropeContentClass is not null && member.LuxembourgRights is null));
        Assert.IsTrue(first.VerifiedSet.Set.Members
            .Where(static member =>
                member.Publisher == Lex.V3.Contracts.PublisherId.LuLegilux &&
                member.BodySha256 is not null)
            .All(static member =>
                member.EuropeContentClass is null &&
                member.LuxembourgRights is not null &&
                member.LuxembourgRights.BoundRunIdentity == member.RunIdentity));
        var reopened = VerifiedLexCorpus6ManifestSet.ParseAndVerify(
            first.ArtifactRef,
            first.VerifiedSet.Set.EuropeSourceSetRef,
            first.VerifiedSet.Set.LuxembourgSourceSetRef,
            first.CanonicalBytes.Span);
        Assert.HasCount(first.VerifiedSet.Set.Members.Count, reopened.Set.Members);
        CollectionAssert.AreEqual(
            first.VerifiedSet.Set.Members.Select(static member => member.ObjectRefSha256).ToArray(),
            reopened.Set.Members.Select(static member => member.ObjectRefSha256).ToArray());
    }

    [TestMethod]
    public async Task StrictReaderRejectsInventedOrUnboundRights()
    {
        var envelope = await CompleteProfileEnvelopeAsync();
        var built = LexCorpus6Builder.TryBuild(
            envelope,
            CompleteEuropeRightsMatrix(envelope.BodyComposition.Envelope.EuropeLegalNoticeEvidence!),
            out var refusal,
            out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");

        var canonical = System.Text.Encoding.UTF8.GetString(built.CanonicalBytes.Span);
        var invented = canonical.Replace(
            "\"basis\":\"cc0\"",
            "\"basis\":\"invented\"",
            StringComparison.Ordinal);
        Assert.AreNotEqual(canonical, invented);
        Assert.ThrowsExactly<ArgumentException>(() => Reopen(invented, built));

        const string binding = "\"bound_run_identity\":{\"resource_id\":\"";
        var bindingStart = canonical.IndexOf(binding, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, bindingStart);
        var shaStart = canonical.IndexOf("\"sha256\":\"", bindingStart, StringComparison.Ordinal) +
            "\"sha256\":\"".Length;
        Assert.IsGreaterThan(bindingStart, shaStart);
        var replacement = canonical[shaStart] == 'a' ? new string('b', 64) : new string('a', 64);
        var unbound = canonical.Remove(shaStart, 64).Insert(shaStart, replacement);
        Assert.ThrowsExactly<ArgumentException>(() => Reopen(unbound, built));
    }

    private static void Reopen(string canonical, LexCorpus6BuildResult built)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(canonical);
        var digest = LexCorpus6Builder.ComputeSha256(bytes);
        var reference = new Lex.V3.Contracts.Source.Core.SourceArtifactRef(
            LexCorpus6Builder.ResourceIdOf(digest),
            digest);
        _ = VerifiedLexCorpus6ManifestSet.ParseAndVerify(
            reference,
            built.VerifiedSet.Set.EuropeSourceSetRef,
            built.VerifiedSet.Set.LuxembourgSourceSetRef,
            bytes);
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
        var akn = await Stage3EvidenceEnvelopeTests.CompleteAknEvidenceAsync(luxembourg);
        var evidence = Stage3EvidenceEnvelope.TryCreateWithEuropeLegalNoticeEvidence(
            europe,
            CompleteLegalNoticeEvidence(),
            luxembourg,
            formex,
            classifications,
            fidelity,
            akn.Inventory,
            akn.LegalContent,
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

    private static Lex.V3.Contracts.Source.Europe.EuRightsMatrix CompleteEuropeRightsMatrix(
        Lex.V3.Contracts.Source.Europe.EuLegalNoticeEvidence notice)
    {
        var evidence = notice.ToArtifactRef(
            "urn:uuid:11111111-1111-1111-1111-111111111111");
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

    private static Lex.V3.Contracts.Source.Europe.EuLegalNoticeEvidence CompleteLegalNoticeEvidence()
    {
        var json =
            "{\"schema\":\"lex-eu-legal-notice-evidence/2\"," +
            "\"requested_uri\":\"https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=en\"," +
            "\"effective_uri\":\"https://eur-lex.europa.eu/content/legal-notice/legal-notice.html?locale=en\"," +
            "\"language_selection\":\"en\"," +
            "\"media_type\":{\"kind\":\"single\",\"value\":\"text/html; charset=UTF-8\"}," +
            "\"observed_date\":{\"kind\":\"single\",\"value\":\"Thu, 03 Sep 2026 16:55:19 GMT\"}," +
            "\"policy_effective_date\":{\"kind\":\"absent\"}," +
            "\"source_policy_version\":{\"kind\":\"absent\"}," +
            "\"byte_length\":135428," +
            $"\"sha256\":\"{new string('a', 64)}\"," +
            $"\"durable_write_receipt_sha256\":\"{new string('b', 64)}\"," +
            $"\"routed_evidence_sha256\":\"{new string('c', 64)}\"," +
            "\"captured_at\":\"2026-09-03T16:55:19.3670000Z\"}\n";
        return Lex.V3.Contracts.Source.Europe.EuLegalNoticeEvidence.ParseAndVerify(
            System.Text.Encoding.UTF8.GetBytes(json));
    }
}
