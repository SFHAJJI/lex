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

        var first = LexCorpus6Builder.TryBuild(envelope, out var firstRefusal, out var firstDetail);
        var second = LexCorpus6Builder.TryBuild(envelope, out var secondRefusal, out var secondDetail);

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
        var luxembourgMembers = first.VerifiedSet.Set.Members
            .Where(static member =>
                member.Publisher == Lex.V3.Contracts.PublisherId.LuLegilux &&
                member.BodySha256 is not null)
            .ToArray();
        Assert.IsTrue(luxembourgMembers.All(static member =>
                member.EuropeContentClass is null &&
                member.LuxembourgRights is not null &&
                member.LuxembourgRights.BindingSha256 ==
                    LexCorpus6LuxembourgRights.ComputeBindingSha256(
                        member.RunIdentity,
                        member.LuxembourgRights.BoundRunIdentity,
                        member.LuxembourgRights.SelectedWemi.IdentitySha256)));
        Assert.IsTrue(luxembourgMembers.Any(static member =>
            member.LuxembourgRights!.BoundRunIdentity != member.RunIdentity));
        Assert.IsTrue(luxembourgMembers.All(static member => member.Stage3Outcomes.Any(
            static outcome => outcome.Domain == LexCorpus6Stage3OutcomeDomain.LuxembourgAknLegalContent)));
        Assert.IsTrue(luxembourgMembers.All(static member => member.Stage3Outcomes.Any(
            static outcome => outcome.Domain == LexCorpus6Stage3OutcomeDomain.LuxembourgPublisherPdfActScope)));
        Assert.HasCount(
            envelope.BodyComposition.Envelope.FormexAnnexClassifications.Classifications
                .Sum(static classification => classification.Members.Count),
            first.VerifiedSet.Set.Members.SelectMany(static member => member.Stage3Outcomes)
                .Where(static outcome => outcome.Domain == LexCorpus6Stage3OutcomeDomain.EuropeAnnexBody)
                .ToArray());
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

    [TestMethod]
    public async Task StrictReaderPinsCanonicalBytesArtifactAndExpectedSourceSets()
    {
        var envelope = await CompleteProfileEnvelopeAsync();
        var built = LexCorpus6Builder.TryBuild(envelope, out var refusal, out var detail);
        Assert.IsNotNull(built, $"{refusal}: {detail}");

        var canonical = System.Text.Encoding.UTF8.GetString(built.CanonicalBytes.Span);
        var noncanonical = System.Text.Encoding.UTF8.GetBytes(canonical.Replace("{\"schema\"", "{ \"schema\"", StringComparison.Ordinal));
        var noncanonicalDigest = LexCorpus6Builder.ComputeSha256(noncanonical);
        Assert.ThrowsExactly<ArgumentException>(() => VerifiedLexCorpus6ManifestSet.ParseAndVerify(
            new Lex.V3.Contracts.Source.Core.SourceArtifactRef(
                LexCorpus6Builder.ResourceIdOf(noncanonicalDigest), noncanonicalDigest),
            built.VerifiedSet.Set.EuropeSourceSetRef,
            built.VerifiedSet.Set.LuxembourgSourceSetRef,
            noncanonical));

        var wrongDigest = new string(built.ArtifactRef.Sha256[0] == 'a' ? 'b' : 'a', 64);
        Assert.ThrowsExactly<ArgumentException>(() => VerifiedLexCorpus6ManifestSet.ParseAndVerify(
            new Lex.V3.Contracts.Source.Core.SourceArtifactRef(built.ArtifactRef.ResourceId, wrongDigest),
            built.VerifiedSet.Set.EuropeSourceSetRef,
            built.VerifiedSet.Set.LuxembourgSourceSetRef,
            built.CanonicalBytes.Span));

        var wrongSource = new Lex.V3.Contracts.Source.Core.SourceArtifactRef(
            "urn:uuid:99999999-9999-5999-8999-999999999999", new string('9', 64));
        Assert.ThrowsExactly<ArgumentException>(() => VerifiedLexCorpus6ManifestSet.ParseAndVerify(
            built.ArtifactRef,
            wrongSource,
            built.VerifiedSet.Set.LuxembourgSourceSetRef,
            built.CanonicalBytes.Span));
    }

    [TestMethod]
    public void EveryLuxembourgRightsDispositionHasOneExplicitTerminalClassification()
    {
        var terminal = new[]
        {
            Lex.V3.Contracts.Source.Luxembourg.LuxembourgRightsChannelDisposition.MissingValue,
            Lex.V3.Contracts.Source.Luxembourg.LuxembourgRightsChannelDisposition.AgreedSameRunCcBy,
            Lex.V3.Contracts.Source.Luxembourg.LuxembourgRightsChannelDisposition.NonAdmittingLicenceScl,
            Lex.V3.Contracts.Source.Luxembourg.LuxembourgRightsChannelDisposition.TypedQuarantineUnruledLicence,
            Lex.V3.Contracts.Source.Luxembourg.LuxembourgRightsChannelDisposition.TypedQuarantineUnrepresentableLicenceShape,
            Lex.V3.Contracts.Source.Luxembourg.LuxembourgRightsChannelDisposition.TypedQuarantineInFileReadingRejected,
        }.ToHashSet();
        foreach (var disposition in Enum.GetValues<Lex.V3.Contracts.Source.Luxembourg.LuxembourgRightsChannelDisposition>())
        {
            Assert.AreEqual(terminal.Contains(disposition), LexCorpus6LuxembourgRights.IsTerminal(disposition),
                disposition.ToString());
        }
    }

    [TestMethod]
    public async Task EveryAnnexClassificationMemberHasOneClosedSemanticOutcome()
    {
        var acquired = await EuFormexAnnexClassificationReconciliationTests.AcquiredFixtureAsync();
        foreach (var member in acquired.Classification.Members)
        {
            var outcome = LexCorpus6Builder.Stage3Outcome(member);
            Assert.AreEqual(LexCorpus6Stage3OutcomeDomain.EuropeAnnexBody, outcome.Domain);
            Assert.AreEqual(member.SemanticIdentitySha256, outcome.SemanticIdentitySha256);
            Assert.IsTrue(Enum.IsDefined(outcome.Disposition));
        }
    }

    [TestMethod]
    public async Task MissingOrForeignRunLegalNoticeRefusesBeforeCorpusBytes()
    {
        var withoutNotice = await CompleteProfileEnvelopeAsync(includeLegalNotice: false);
        Assert.IsNull(LexCorpus6Builder.TryBuild(withoutNotice, out var refusal, out _));
        Assert.AreEqual(LexCorpus6BuildRefusal.EuropeRightsEvidenceMissing, refusal);

        var europe = withoutNotice.BodyComposition.Envelope.Europe;
        var (route, request) = CompleteLegalNoticeRoute(
            new Lex.V3.Contracts.Source.Core.SourceArtifactRef(
                "urn:uuid:99999999-9999-5999-8999-999999999999", new string('9', 64)));
        var source = withoutNotice.BodyComposition.Envelope;
        var rejected = Stage3EvidenceEnvelope.TryCreateWithEuropeLegalNoticeRoute(
            europe, route, request, source.Luxembourg, source.Formex,
            source.FormexAnnexClassifications, source.FidelityPreservation,
            source.LuxembourgAknArticleInventoryPopulation,
            source.LuxembourgAknLegalContentPopulation,
            out var envelopeRefusal, out _);
        Assert.IsNull(rejected);
        Assert.AreEqual(Stage3EvidenceEnvelopeRefusal.EuropeLegalNoticeRunMismatch, envelopeRefusal);
    }

    [TestMethod]
    public async Task DeliveredEuropeWithoutContentClassBindingsRefusesBeforeCorpusBytes()
    {
        var envelope = await CompleteProfileEnvelopeAsync(stripEuropeContentClasses: true);
        Assert.IsNull(LexCorpus6Builder.TryBuild(envelope, out var refusal, out _));
        Assert.AreEqual(LexCorpus6BuildRefusal.EuropeRightsBindingMissing, refusal);
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

    private static async Task<Stage3DerivationProfileEnvelope> CompleteProfileEnvelopeAsync(
        bool includeLegalNotice = true,
        bool stripEuropeContentClasses = false)
    {
        var europe = await EuAxiomWiringHarness.RunAsync(
            static root => EuAcquisitionTestFixture.AxiomAbsenceScriptFor(root));
        if (stripEuropeContentClasses)
        {
            europe = WithoutHeldBodyContentClasses(europe);
        }
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
        Stage3EvidenceEnvelope? evidence;
        Stage3EvidenceEnvelopeRefusal evidenceRefusal;
        string? evidenceDetail;
        if (includeLegalNotice)
        {
            var (route, request) = CompleteLegalNoticeRoute(
                europe.CorpusRecordSet!.Set.Records[0].RunIdentity);
            evidence = Stage3EvidenceEnvelope.TryCreateWithEuropeLegalNoticeRoute(
                europe, route, request, luxembourg, formex, classifications, fidelity,
                akn.Inventory, akn.LegalContent, out evidenceRefusal, out evidenceDetail);
        }
        else
        {
            evidence = Stage3EvidenceEnvelope.TryCreate(
                europe, luxembourg, formex, classifications, fidelity,
                akn.Inventory, akn.LegalContent, out evidenceRefusal, out evidenceDetail);
        }
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

    private static Europe.EuQueryExecutionResult WithoutHeldBodyContentClasses(
        Europe.EuQueryExecutionResult source)
    {
        var corpus = source.CorpusRecordSet!;
        var entries = corpus.Set.Records.Select(record =>
            new CorpusRecordOutcomeEntry(
                record.ObjectRef,
                record.ObjectOrdinal,
                record.Body.Kind switch
                {
                    Lex.V3.Contracts.Source.Corpus.CorpusBodyRecordKind.Held =>
                        CorpusRecordOutcomeKind.Held,
                    Lex.V3.Contracts.Source.Corpus.CorpusBodyRecordKind.NotHeld =>
                        CorpusRecordOutcomeKind.NotHeld,
                    _ => CorpusRecordOutcomeKind.PendingAcquisition,
                },
                record.Body.NotHeldReason,
                record.Body.PendingAcquisitionReason?.Kind,
                record.Body.PendingAcquisitionReason?.Refusal)).ToArray();
        var completion = new CorpusRecordSetCompletion(
            CorpusRecordSetCompletionState.Complete,
            entries.Length,
            entries);
        var recordSetResult = CorpusRecordSetWriteResult.Written(
            source.CorpusRecordSetRef!,
            source.CorpusRecordSetReceipt!,
            corpus,
            completion,
            Lex.V3.Contracts.Custody.CustodyMembership.Floored);
        return Europe.EuQueryExecutionResult.DeliveredWithLocatedAmendments(
            source.Topology,
            source.FamilyOutcomes,
            source.ObservedObjectCount,
            source.ObservedExpressionCount,
            source.ReductionExclusions,
            source.WatermarkWitnessPlan!,
            source.RootBinding!,
            source.WitnessReconciliation!,
            source.WitnessTerminations!,
            source.ScopeManifestReceipt!,
            source.ScopeManifestCanonicalSha256!,
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

    private static (
        Lex.V3.Contracts.Source.Http.RoutedHttpEvidence Evidence,
        Lex.V3.Contracts.Source.Http.HttpLogicalRequest Request) CompleteLegalNoticeRoute(
            Lex.V3.Contracts.Source.Core.SourceArtifactRef runIdentity)
    {
        const string sha = "489c635573a9c4eb39e30702d0c2a62eaaff4632a0bd9b7e300d6e2a3111861f";
        const ulong length = 135_428;
        var emptySha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([])).ToLowerInvariant();
        var request = Lex.V3.Contracts.Source.Http.HttpLogicalRequest.Create(
            Lex.V3.Contracts.Source.Europe.EuLegalNoticeEvidence.RequestedUri,
            Lex.V3.Contracts.Source.Core.HttpRequestMethod.Get,
            [new Lex.V3.Contracts.Source.Http.HttpLogicalRequestHeader(
                "user-agent", "Lex/0.1 (+https://github.com/SFHAJJI/lex)")],
            new Lex.V3.Contracts.Source.Http.HttpLogicalRequestBody(0, emptySha),
            new string('1', 64), new string('2', 64));
        var requestDigest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            request.CopyCanonicalBytes())).ToLowerInvariant();
        var reference = new Lex.V3.Contracts.Custody.DurableBlobRef(
            Lex.V3.Contracts.Custody.CustodySchemaIds.DurableBlobRef,
            sha, checked((long)length), Lex.V3.Contracts.Custody.CustodyClass.NightlyFloor90d);
        var policy = new Lex.V3.Contracts.Custody.CustodyPolicyEvidence(
            Lex.V3.Contracts.Custody.CustodySchemaIds.CustodyPolicyEvidence,
            reference,
            Lex.V3.Contracts.Custody.CustodyVerificationProfile.FileSystemUnenforced1,
            null,
            Lex.V3.Contracts.Custody.CustodyProtection.NotEnforced,
            new DateTimeOffset(2026, 9, 3, 16, 55, 0, TimeSpan.Zero),
            null);
        var receipt = new Lex.V3.Contracts.Custody.DurableBlobWriteReceipt(
            Lex.V3.Contracts.Custody.CustodySchemaIds.DurableBlobWriteReceipt, reference, policy);
        var receiptDigest = Lex.V3.Contracts.Custody.DurableBlobWriteReceiptDigest.Of(receipt);
        var absent = new Lex.V3.Contracts.Source.Http.RoutedHttpAbsentHeader();
        var headers = new Lex.V3.Contracts.Source.Http.RoutedHttpResponseHeaders(
            new Lex.V3.Contracts.Source.Http.RoutedHttpSingleHeader("text/html; charset=UTF-8"),
            new Lex.V3.Contracts.Source.Http.RoutedHttpSingleHeader("135428"),
            absent, absent, absent, absent, absent, absent, absent, absent,
            new Lex.V3.Contracts.Source.Http.RoutedHttpSingleHeader("Thu, 03 Sep 2026 16:55:19 GMT"),
            absent, absent);
        var hop = Lex.V3.Contracts.Source.Http.RoutedHttpHop.Create(
            0, "urn:uuid:11111111-1111-5111-8111-111111111111", null, requestDigest,
            Lex.V3.Contracts.Source.Europe.EuLegalNoticeEvidence.RequestedUri, 200, headers,
            "2026-09-03T16:55:19.3670000Z", "2026-09-03T16:55:19.3670000Z",
            new Lex.V3.Contracts.Source.Http.DeclaredContentLengthHttpCompletion(length),
            length, sha, receiptDigest, length, sha);
        var evidence = Lex.V3.Contracts.Source.Http.RoutedHttpEvidence.Create(
            runIdentity, 1, 0, [hop], new Lex.V3.Contracts.Source.Http.CompleteHttpRouteOutcome(),
            new Dictionary<string, Lex.V3.Contracts.Custody.DurableBlobWriteReceipt>
            {
                [hop.ObservationId] = receipt,
            });
        return (evidence, request);
    }
}
