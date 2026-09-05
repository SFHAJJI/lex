using Lex.V3.Tests.Contracts.Source.Absence;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Lex.V3.Artifacts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;
using Lex.V3.Ingest.Luxembourg;

namespace Lex.V3.Ingest.Tests;

/// <summary>
/// D1-04, the Luxembourg query-execution adapter: one test per typed refusal and per Decision 64
/// acquisition state, plus one real, fresh <see cref="FileSystemCustodyStore"/> end-to-end run.
///
/// <para>
/// Refreeze (lex-event-20260903T221036088Z-963c186c93cc4c898eec91ee9f2b91e9): the review objection
/// was that nothing tied a caller's <c>observations</c> to what a run's own family enumeration
/// actually delivered, and this file was the proof -- every test either passed a non-empty family
/// list with empty observations, or a non-empty observation list with an empty family list, never
/// both. Every test below that supplies a non-empty <c>observations</c> list now also runs a real
/// resource-observation family delivering the matching row count and designates it, so the new
/// census guard in <c>LuxembourgQueryExecutionAdapter.RunAsync</c> is satisfied rather than masking
/// the scenario each test actually means to drive.
/// </para>
/// </summary>
[TestClass]
public sealed class LuxembourgQueryExecutionAdapterTests
{
    private const string RelationSetId = "G";
    private const string RelationFamilyKey = "relation-assertions";
    private const string ResourceSetId = "S";
    private const string ResourceFamilyKey = "resource-observations";
    private const string AssertionSetId = "A";
    private const string AssertionFamilyKey = "resource-assertions";
    private const string JoluxAct = "http://data.legilux.public.lu/resource/ontology/jolux#Act";
    private const string JoluxLegalResource =
        "http://data.legilux.public.lu/resource/ontology/jolux#LegalResource";
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string TypeDocumentPredicate =
        "http://data.legilux.public.lu/resource/ontology/jolux#typeDocument";
    private const string TypeDocumentPrefix =
        "http://data.legilux.public.lu/resource/authority/resource-type/";
    private const string CitesPredicate = "http://data.legilux.public.lu/resource/ontology/jolux#cites";

    [TestMethod]
    public async Task TopologyIsAlwaysMintedEvenOnRefusal()
    {
        var (profile, _, enumerationRef) = BuildProfile();
        var adapter = new LuxembourgQueryExecutionAdapter(
            new InMemoryCustodyStore(), NewExecutor(new InMemoryCustodyStore(), NoSendHandler()),
            profile);

        var result = await adapter.RunAsync(
            [], null, null, null, new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

        Assert.IsNotNull(result.Topology);
        Assert.AreEqual(
            LuxembourgSourceProfileTopology.SinglePublisherStoreMemberKey,
            result.Topology.Topology.MemberKey);
        Assert.AreEqual(profile.ScopeBinding.SourceProfileRef, result.Topology.IdentityProfileRef);

        // Zero families enumerated still delivers (an empty run over a floored store), and its
        // FamilyOutcomes list is empty -- the one case that tells All() and Any() apart in the
        // Completion computation. Vacuously "every family proven" is the honest reading of "there
        // were no families to fail", exactly as an empty acquisition report is not a partial one.
        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.AreEqual(0, result.FamilyOutcomes.Count);
        Assert.AreEqual(LuxembourgQueryExecutionCompletion.AllFamiliesProven, result.Completion);

        // D1-06c-LU-2 item 5: the run's own corpus/6 record set, written as its last step. Before
        // this slice nothing in the Luxembourg lane ever called CorpusRecordSetWriter.WriteAsync, so
        // no LU run durably wrote a record set at all. The set here is the one the writer itself
        // reopened and verified, never the in-memory set this run computed.
        Assert.IsNotNull(result.CorpusRecordSetRef);
        Assert.IsNotNull(result.CorpusRecordSet);
        Assert.IsNotNull(result.DocumentAcquisitionOutcomesByOrdinal);
        Assert.AreEqual(
            result.ScopeManifestCanonicalSha256,
            result.CorpusRecordSet!.Set.ManifestRef.Sha256,
            "the set must name THIS run's own manifest, by that manifest's own canonical digest.");
        Assert.AreEqual(
            result.ScopeManifestReceipt!.Reference.ContentSha256,
            result.CorpusRecordSet.Set.RunIdentity.Sha256,
            "and this run's own identity is paired with real evidence this run produced, not an "
            + "inert placeholder.");
    }

    [TestMethod]
    public void NotCompleteRefusesTheAcquiredCompleteState()
    {
        Assert.ThrowsExactly<ArgumentException>(static () =>
            LuxembourgRelationFamilyAcquisition.NotComplete(
                "urn:example:predicate",
                LuxembourgRelationFamilyAcquisitionState.AcquiredComplete,
                "a reason"));
    }

    [TestMethod]
    public void ProofRefusedRequiresARealRefusalCode()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(static () =>
            LuxembourgFamilyEnumerationOutcome.ProofRefused(
                "family", AbsenceFamilyEnumerationProofRefusal.None));
    }

    [TestMethod]
    public void ARefusalDetailRequiresARealRefusalCode()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(static () =>
            new LuxembourgQueryExecutionRefusalDetail(LuxembourgQueryExecutionRefusal.None, null, null));
    }

    [TestMethod]
    public void AResolutionFailureCanOnlyAccompanyScopeResolutionFailed()
    {
        var failure = new LuxembourgProfileResolutionFailure(
            LuxembourgProfileResolutionFailureCode.EvidenceBindingRejected, "urn:example:subject");
        Assert.ThrowsExactly<ArgumentException>(() =>
            new LuxembourgQueryExecutionRefusalDetail(
                LuxembourgQueryExecutionRefusal.ScopeManifestNotRetained, failure, null));
        Assert.ThrowsExactly<ArgumentException>(static () =>
            new LuxembourgQueryExecutionRefusalDetail(
                LuxembourgQueryExecutionRefusal.ScopeResolutionFailed, null, null));
    }

    [TestMethod]
    public async Task ACensusFamilyKeyWithNoAssertionFamilyKeyThrows()
    {
        // The "both or neither" guard, first direction: S named, A withheld. Thrown before this run
        // ever touches the executor or the custody store -- a caller-contract violation, not a
        // domain refusal, so no family enumeration should even be attempted.
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var adapter = new LuxembourgQueryExecutionAdapter(
            store, NewExecutor(store, NoSendHandler()), profile);

        var exception = await Assert.ThrowsExactlyAsync<ArgumentException>(() => adapter.RunAsync(
            [], null, ResourceFamilyKey, null, new PermissiveEvidenceResolver(enumerationRef),
            DocumentFetchRendererSource(),
            CancellationToken.None));
        StringAssert.Contains(exception.Message, "both");
    }

    [TestMethod]
    public async Task AnAssertionFamilyKeyWithNoCensusFamilyKeyThrows()
    {
        // The other direction: A named, S withheld.
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var adapter = new LuxembourgQueryExecutionAdapter(
            store, NewExecutor(store, NoSendHandler()), profile);

        var exception = await Assert.ThrowsExactlyAsync<ArgumentException>(() => adapter.RunAsync(
            [], null, null, AssertionFamilyKey, new PermissiveEvidenceResolver(enumerationRef),
            DocumentFetchRendererSource(),
            CancellationToken.None));
        StringAssert.Contains(exception.Message, "both");
    }

    [TestMethod]
    public async Task NoRelationFamilyDesignatedMeansUnacquired()
    {
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var adapter = new LuxembourgQueryExecutionAdapter(
            store, NewExecutor(store, NoSendHandler()), profile);

        var result = await adapter.RunAsync(
            [], null, null, null, new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

        Assert.AreEqual(18, result.RelationFamilyAcquisitions.Count);
        foreach (var acquisition in result.RelationFamilyAcquisitions)
        {
            Assert.AreEqual(LuxembourgRelationFamilyAcquisitionState.Unacquired, acquisition.State);
            Assert.IsNull(acquisition.CompletionEvidence);
            Assert.IsNotNull(acquisition.Reason);
        }
    }

    [TestMethod]
    public async Task ADesignatedFamilyKeyNeverRequestedIsUncertain()
    {
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var adapter = new LuxembourgQueryExecutionAdapter(
            store, NewExecutor(store, NoSendHandler()), profile);

        var result = await adapter.RunAsync(
            [], RelationFamilyKey, null, null, new PermissiveEvidenceResolver(enumerationRef),
            DocumentFetchRendererSource(),
            CancellationToken.None);

        Assert.AreEqual(18, result.RelationFamilyAcquisitions.Count);
        foreach (var acquisition in result.RelationFamilyAcquisitions)
        {
            Assert.AreEqual(LuxembourgRelationFamilyAcquisitionState.Uncertain, acquisition.State);
            Assert.IsNull(acquisition.CompletionEvidence);
            StringAssert.Contains(acquisition.Reason, RelationFamilyKey);
        }
    }

    [TestMethod]
    public async Task ARobotsDisallowOnTheRelationFamilyIsIncomplete()
    {
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var handler = new LuxembourgAcquisitionTestFixture.SequencedHandler(
            (_, req) => TextResponse(req, "User-agent: *\nDisallow: /\n"));
        var adapter = new LuxembourgQueryExecutionAdapter(
            store, NewExecutor(store, handler), profile);
        var (partitionRequest, witness) = BuildPartitionRequest(RelationSetId, RelationFamilyKey);

        var result = await adapter.RunAsync(
            [(partitionRequest, witness, null)], RelationFamilyKey, null, null,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

        Assert.AreEqual(1, result.FamilyOutcomes.Count);
        Assert.AreEqual(LuxembourgFamilyEnumerationOutcomeKind.ExecutorRefused, result.FamilyOutcomes[0].Kind);
        Assert.AreEqual(
            LuxembourgEnumerationRefusal.RobotsBootstrapRefused,
            result.FamilyOutcomes[0].ExecutorRefusal!.Code);
        foreach (var acquisition in result.RelationFamilyAcquisitions)
        {
            Assert.AreEqual(LuxembourgRelationFamilyAcquisitionState.Incomplete, acquisition.State);
            StringAssert.Contains(acquisition.Reason, "executor_refused");
        }
    }

    [TestMethod]
    public async Task AProvenRelationFamilyMakesEveryPredicateAcquiredCompleteWithTheSameEvidence()
    {
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var handler = RelationFamilyDeliveringHandler();
        var adapter = new LuxembourgQueryExecutionAdapter(
            store, NewExecutor(store, handler), profile);
        var (partitionRequest, witness) = BuildPartitionRequest(RelationSetId, RelationFamilyKey);

        var result = await adapter.RunAsync(
            [(partitionRequest, witness, null)], RelationFamilyKey, null, null,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

        Assert.AreEqual(1, result.FamilyOutcomes.Count);
        Assert.AreEqual(
            LuxembourgFamilyEnumerationOutcomeKind.Proven, result.FamilyOutcomes[0].Kind,
            $"executorRefusal={result.FamilyOutcomes[0].ExecutorRefusal?.Code} " +
            $"detail={result.FamilyOutcomes[0].ExecutorRefusal?.CoreRefusalDetail} " +
            $"proofRefusal={result.FamilyOutcomes[0].ProofRefusal}");
        Assert.AreEqual(18, result.RelationFamilyAcquisitions.Count);
        var expectedPredicates = profile.RelationRules.Select(static rule => rule.PredicateIri).ToHashSet();
        Assert.AreEqual(18, expectedPredicates.Count);
        foreach (var acquisition in result.RelationFamilyAcquisitions)
        {
            Assert.AreEqual(LuxembourgRelationFamilyAcquisitionState.AcquiredComplete, acquisition.State);
            Assert.AreSame(result.FamilyOutcomes[0].Proof, acquisition.CompletionEvidence);
            Assert.IsNull(acquisition.Reason);
            Assert.IsTrue(expectedPredicates.Remove(acquisition.PredicateIri));
        }

        Assert.AreEqual(0, expectedPredicates.Count, "every relation predicate must appear exactly once");
        Assert.IsNotNull(result.ScopeManifestReceipt);
        Assert.IsNull(result.Refusal);
        Assert.AreEqual(LuxembourgQueryExecutionCompletion.AllFamiliesProven, result.Completion);
    }

    [TestMethod]
    public async Task AMismatchedSelectionProducesAProofRefusedOutcome()
    {
        // Fold-in one's ProofRefused case, which no test reached before this refreeze: the
        // executor delivers a receipt (custody is sound), but the count claims one selection and
        // the page delivers a different one, so Source/Core reports DifferentSelections and
        // AbsenceFamilyEnumerationProof.TryCreate refuses PassesDeliveredDifferentSelections. The
        // handler shape is the executor's own proven pattern for this
        // (LuxembourgRepeatedEnumerationExecutorTests.ACountOneBelowTheCeilingProceedsToPageZero).
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 or 3 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(999_999)),
            // The relation-assertions template's own closed projection (subject, predicate, object,
            // key_1..key_6), not the generic key_1..key_6-only shape: this family is SetId "G".
            _ => LuxembourgAcquisitionTestFixture.JsonResponse(req, RelationAssertionsRowsJson()),
        });
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (partitionRequest, witness) = BuildPartitionRequest(RelationSetId, RelationFamilyKey);

        var result = await adapter.RunAsync(
            [(partitionRequest, witness, null)], RelationFamilyKey, null, null,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

        Assert.AreEqual(1, result.FamilyOutcomes.Count);
        Assert.AreEqual(LuxembourgFamilyEnumerationOutcomeKind.ProofRefused, result.FamilyOutcomes[0].Kind);
        Assert.AreEqual(
            AbsenceFamilyEnumerationProofRefusal.PassesDeliveredDifferentSelections,
            result.FamilyOutcomes[0].ProofRefusal);
    }

    [TestMethod]
    public async Task ADeliveredResultWithARefusedFamilyReportsPartialCompletion()
    {
        // Fold-in one's other required case: a Delivered result is legal even when a family this
        // run enumerated was refused, because nothing about writing and holding the scope manifest
        // depends on every family being Proven. Completion is the unmissable signal a consumer
        // reads before treating this as a complete run.
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 or 3 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(999_999)),
            // The relation-assertions template's own closed projection (subject, predicate, object,
            // key_1..key_6), not the generic key_1..key_6-only shape: this family is SetId "G".
            _ => LuxembourgAcquisitionTestFixture.JsonResponse(req, RelationAssertionsRowsJson()),
        });
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (partitionRequest, witness) = BuildPartitionRequest(RelationSetId, RelationFamilyKey);

        var result = await adapter.RunAsync(
            [(partitionRequest, witness, null)], null, null, null,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.IsNotNull(result.ScopeManifestReceipt, "a refused family does not stop the manifest write");
        Assert.AreEqual(LuxembourgFamilyEnumerationOutcomeKind.ProofRefused, result.FamilyOutcomes.Single().Kind);
        Assert.AreEqual(LuxembourgQueryExecutionCompletion.PartialFamilyRefused, result.Completion);
    }

    [TestMethod]
    public async Task ADesignatedResourceFamilyNeverEnumeratedRefusesWithoutDeriving()
    {
        // D1-04b: a resource family IS named, but this run's family list never actually enumerated
        // it (a caller/config mismatch, exactly as ADesignatedFamilyKeyNeverRequestedIsUncertain
        // covers for the relation family -- except here derived observations are actually at stake,
        // so the honest answer is a hard refusal, not an "uncertain" acquisition state). There is no
        // caller-supplied observation to construct any more: RunAsync itself would have nothing to
        // derive from, so it must refuse before ever trying.
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var adapter = new LuxembourgQueryExecutionAdapter(
            store, NewExecutor(store, NoSendHandler()), profile);

        var result = await adapter.RunAsync(
            [], null, ResourceFamilyKey, AssertionFamilyKey, new PermissiveEvidenceResolver(enumerationRef),
            DocumentFetchRendererSource(),
            CancellationToken.None);

        Assert.IsNull(result.ScopeManifestReceipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgQueryExecutionRefusal.ResourceObservationFamilyNotProven, result.Refusal.Code);
    }

    [TestMethod]
    public async Task ADesignatedResourceFamilyThatIsFoundButNotProvenRefusesWithoutDeriving()
    {
        // The other way the guard can fail, distinct from "never enumerated": the family key IS
        // found among this run's outcomes, but its enumeration did not prove complete (mirroring
        // AMismatchedSelectionProducesAProofRefusedOutcome's own handler). A guard that only checked
        // "was this key found" and not "was it Proven" would try to reopen rows behind a family this
        // run never actually finished censusing.
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 or 3 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(999_999)),
            _ => LuxembourgAcquisitionTestFixture.JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
        });
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (partitionRequest, witness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);

        var result = await adapter.RunAsync(
            [(partitionRequest, witness, null)], null, ResourceFamilyKey, AssertionFamilyKey,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

        Assert.AreEqual(LuxembourgFamilyEnumerationOutcomeKind.ProofRefused, result.FamilyOutcomes.Single().Kind);
        Assert.IsNull(result.ScopeManifestReceipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgQueryExecutionRefusal.ResourceObservationFamilyNotProven, result.Refusal.Code);
    }

    [TestMethod]
    public async Task TheResourceDerivationMatchesTheDesignatedFamilyByKeyNotByPosition()
    {
        // Three families enumerated in one run, all Proven, the designated census family listed
        // SECOND and the designated assertion family listed THIRD (neither first). A derivation that
        // picked "the first Proven outcome" or "any Proven outcome" rather than the outcome whose
        // FamilyKey equals resourceObservationFamilyKey/resourceAssertionsFamilyKey would try to
        // decode the wrong family's own rows and disagree with that family's own proof, refusing
        // instead of succeeding.
        //
        // Each family is its own RoutedHttpAcquisitionSession (LuxembourgRepeatedEnumerationExecutor
        // .RunPartitionAsync starts a fresh one every call), and SequencedHandler's ordinal counter
        // is one running total across the whole shared handler instance, so this handler answers
        // robots three times -- once at ordinal 0 for the relation family's own bootstrap, once at
        // ordinal 7 for the census family's, once at ordinal 14 for the assertion family's.
        const string subjectUri = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a0";
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var (relationRequest, relationWitness) = BuildPartitionRequest(RelationSetId, RelationFamilyKey);
        var (resourceRequest, resourceWitness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var (assertionRequest, assertionWitness) = BuildPartitionRequest(AssertionSetId, AssertionFamilyKey);
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            7 or 14 => TextResponse(req, "User-agent: *\nAllow: /\n"),
            1 or 4 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(2)),
            2 or 5 => LuxembourgAcquisitionTestFixture.JsonResponse(req, RelationAssertionsRowsJson("a", "b")),
            3 or 6 => LuxembourgAcquisitionTestFixture.JsonResponse(req, RelationAssertionsRowsJson()),
            8 or 11 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            9 or 12 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.RowsJson(subjectUri)),
            10 or 13 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            15 or 18 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            // A relation-predicate row: real content the identity check must still admit as a member
            // of the census, even though BuildResourceObservations' own vocabulary filter then
            // excludes it from Assertions (that filter is exercised for its own sake by the two
            // AcceptedCandidate-restoring tests below; this test's own concern is family selection by
            // key, not assertion content).
            16 or 19 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, AssertionRowsJson(
                    (subjectUri, CitesPredicate, subjectUri, "iri", "", ""))),
            17 or 20 => LuxembourgAcquisitionTestFixture.JsonResponse(req, AssertionRowsJson()),
            _ => throw new AssertFailedException($"unexpected ordinal {ordinal}"),
        });
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);

        var result = await adapter.RunAsync(
            [(relationRequest, relationWitness, null), (resourceRequest, resourceWitness, null),
                (assertionRequest, assertionWitness, null)],
            RelationFamilyKey, ResourceFamilyKey, AssertionFamilyKey,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

        Assert.AreEqual(3, result.FamilyOutcomes.Count);
        Assert.IsTrue(
            result.FamilyOutcomes.All(
                static outcome => outcome.Kind == LuxembourgFamilyEnumerationOutcomeKind.Proven),
            string.Join(
                ", ",
                result.FamilyOutcomes.Select(static outcome =>
                    $"{outcome.FamilyKey}={outcome.Kind}/{outcome.ExecutorRefusal?.Code}/{outcome.ProofRefusal}")));
        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.IsNotNull(result.ScopeManifestReceipt);
        Assert.AreEqual(LuxembourgQueryExecutionCompletion.AllFamiliesProven, result.Completion);
    }

    [TestMethod]
    public async Task AProvenResourceFamilysRowsDeriveObservationsAndLetScopeResolutionProceed()
    {
        // The positive control: two genuinely delivered, independently re-verified census rows,
        // decoded through item 17 into two LuxembourgResourceObservation values with no
        // caller-supplied list anywhere in the call. Both this run's census keys end up with empty
        // assertions, for two DIFFERENT honest reasons the ruling names explicitly: a0 has a real "A"
        // row whose predicate (a RelationPredicate, not an AssertionPredicate) BuildResourceObservations'
        // own vocabulary filter excludes, while a1 has no "A" row at all. Both are the identity-set
        // membership check passing, not it being skipped: a0's subject must still be found in the
        // census's own delivered key set before its row is even looked at.
        const string subjectA0 = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a0";
        const string subjectA1 = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1";
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 or 4 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(2)),
            2 or 5 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.RowsJson(subjectA0, subjectA1)),
            3 or 6 => LuxembourgAcquisitionTestFixture.JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            7 => TextResponse(req, "User-agent: *\nAllow: /\n"),
            8 or 11 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            9 or 12 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, AssertionRowsJson((subjectA0, CitesPredicate, subjectA1, "iri", "", ""))),
            10 or 13 => LuxembourgAcquisitionTestFixture.JsonResponse(req, AssertionRowsJson()),
            _ => throw new AssertFailedException($"unexpected ordinal {ordinal}"),
        });
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (resourceRequest, resourceWitness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var (assertionRequest, assertionWitness) = BuildPartitionRequest(AssertionSetId, AssertionFamilyKey);

        var result = await adapter.RunAsync(
            [(resourceRequest, resourceWitness, null), (assertionRequest, assertionWitness, null)],
            null, ResourceFamilyKey, AssertionFamilyKey,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.IsNotNull(result.ScopeManifestReceipt);
        Assert.AreEqual(LuxembourgQueryExecutionCompletion.AllFamiliesProven, result.Completion);

        // The exact set of derived subjects, and their count: both census keys, in delivery order,
        // never a subset or a superset. This is the field the review objection asked for: the only
        // way a test could previously see "what did this run actually derive" was to reason
        // backwards from Completion/Refusal being null, which cannot tell "derived the right set"
        // from "derived nothing and got lucky".
        CollectionAssert.AreEqual(new[] { subjectA0, subjectA1 }, result.ResourceObservationSubjects.ToArray());
        Assert.AreEqual(2, result.ResourceObservationSubjects.Count);

        // a0's own empty assertion list is not "no rows seen": it is one real "A" row, actively
        // excluded because CitesPredicate is a RelationPredicate, not an AssertionPredicate. The new
        // typed accounting is exactly what makes that distinction inspectable -- without it, a0
        // would look identical to a1 below, which really did see zero rows.
        Assert.AreEqual(1, result.ResourceObservationExclusions.Count);
        var exclusion = result.ResourceObservationExclusions[0];
        Assert.AreEqual(subjectA0, exclusion.Subject);
        Assert.AreEqual(LuxembourgResourceObservationExclusionCause.PredicateNotAdmitted, exclusion.Cause);
        Assert.AreEqual(1, exclusion.RowCount);

        // a1 carries no exclusion entry at all: it never had an "A" row to exclude in the first
        // place, the other of the two "honest reasons" this test's own derivation must tell apart.
        Assert.IsFalse(result.ResourceObservationExclusions.Any(e => e.Subject == subjectA1));
    }

    [TestMethod]
    public async Task ABlankNodeObjectOnAnAdmittedPredicateIsExcludedAndAccountedForRatherThanFailingTheRun()
    {
        // The review objection's second exclusion cause, not driven by any prior test: an "A" row
        // whose predicate IS admitted (unlike AProvenResourceFamilysRowsDeriveObservationsAndLet
        // ScopeResolutionProceed's own PredicateNotAdmitted row above) but whose object_kind is the
        // query plan's own "unsupported_blank_node" marker. LuxembourgObservedAssertion has no
        // member that can carry a blank node, so this row is excluded, accounted for under
        // BlankNodeObject, and never reaches an uncaught exception or a silent drop.
        const string subjectUri = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a0";
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var assertionPage = AssertionRowsJson(
            (subjectUri, TypeDocumentPredicate, "_:b0", "unsupported_blank_node", "", ""));
        var handler = TwoFamilyDeliveringHandler([subjectUri], 1, assertionPage);
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (resourceRequest, resourceWitness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var (assertionRequest, assertionWitness) = BuildPartitionRequest(AssertionSetId, AssertionFamilyKey);

        var result = await adapter.RunAsync(
            [(resourceRequest, resourceWitness, null), (assertionRequest, assertionWitness, null)],
            null, ResourceFamilyKey, AssertionFamilyKey,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        CollectionAssert.AreEqual(new[] { subjectUri }, result.ResourceObservationSubjects.ToArray());
        Assert.AreEqual(1, result.ResourceObservationExclusions.Count);
        var exclusion = result.ResourceObservationExclusions[0];
        Assert.AreEqual(subjectUri, exclusion.Subject);
        Assert.AreEqual(LuxembourgResourceObservationExclusionCause.BlankNodeObject, exclusion.Cause);
        Assert.AreEqual(1, exclusion.RowCount);
    }

    /// <summary>
    /// The manifest gate's REFUSAL direction: a genuine custody failure on the manifest bytes still
    /// refuses with <see cref="LuxembourgQueryExecutionRefusal.ScopeManifestNotRetained"/>, and never
    /// resolves into a weaker custody class.
    /// </summary>
    /// <remarks>
    /// The gate was re-conditioned so an unenforced store no longer refuses, and only its ACCEPTING
    /// direction was driven. That is the same asymmetry that let the absent-artifact mutation
    /// survive 290 of 290 earlier in this lane: a guard whose refusal side nothing exercises is a
    /// guard proven by nothing. Here the store accepts the manifest write and then cannot reproduce
    /// those bytes at their own digest, which is a real failure rather than a weaker guarantee.
    /// </remarks>
    [TestMethod]
    public async Task AScopeManifestThatCannotBeReproducedAtItsOwnDigestStillRefuses()
    {
        var (profile, _, enumerationRef) = BuildProfile();
        var root = Path.Combine(Path.GetTempPath(), "lex-lu-manifest-hold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new ManifestHoldFailingCustodyStore(new FileSystemCustodyStore(root));
            var adapter = new LuxembourgQueryExecutionAdapter(
                store, NewExecutor(store, NoSendHandler()), profile);

            var result = await adapter.RunAsync(
                [], null, null, null, new PermissiveEvidenceResolver(enumerationRef),
                DocumentFetchRendererSource(), CancellationToken.None);

            Assert.IsNotNull(result.Refusal);
            Assert.AreEqual(
                LuxembourgQueryExecutionRefusal.ScopeManifestNotRetained,
                result.Refusal!.Code,
                "a manifest whose stored bytes do not reopen at their own digest is NOT held.");
            StringAssert.Contains(result.Refusal.Detail, "digest");
            Assert.IsNull(
                result.ScopeManifestReceipt,
                "and no receipt is reported for it under any custody class.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// The manifest REOPEN had no catch at all, so a CustodyIntegrityException raised there
    /// escaped RunAsync UNTYPED, past every typed refusal this adapter exists to produce and past
    /// the principle the two tests either side of it assert by name. A store that accepts the
    /// write, satisfies the hold's own verification read, and then cannot reproduce those bytes
    /// on the NEXT read of the same digest drives exactly that path.
    /// </summary>
    /// <remarks>
    /// The escape is asserted rather than left to the runner. An unhandled exception would fail
    /// this test anyway, but not by NAME, and the whole point of the defect is that an untyped
    /// escape is the checkability claim failing where it is least visible.
    /// </remarks>
    [TestMethod]
    public async Task AScopeManifestThatFailsIntegrityOnTheReopenRefusesRatherThanEscaping()
    {
        var (profile, _, enumerationRef) = BuildProfile();
        var root = Path.Combine(Path.GetTempPath(), "lex-lu-manifest-reopen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new ManifestReopenFailingCustodyStore(new FileSystemCustodyStore(root));
            var adapter = new LuxembourgQueryExecutionAdapter(
                store, NewExecutor(store, NoSendHandler()), profile);

            LuxembourgQueryExecutionResult result;
            try
            {
                result = await adapter.RunAsync(
                    [], null, null, null, new PermissiveEvidenceResolver(enumerationRef),
                    DocumentFetchRendererSource(), CancellationToken.None);
            }
            catch (CustodyIntegrityException exception)
            {
                Assert.Fail(
                    "a custody integrity failure on the manifest reopen escaped RunAsync untyped: "
                    + exception.Message);
                throw;
            }

            Assert.IsNotNull(result.Refusal);
            Assert.AreEqual(
                LuxembourgQueryExecutionRefusal.ScopeManifestNotRetained,
                result.Refusal!.Code,
                "a manifest that does not reopen at its own digest is NOT held.");
            StringAssert.Contains(
                result.Refusal.Detail,
                "could not be reopened at its own digest",
                "and the refusal carries the store's own failure rather than a generic one.");
            Assert.IsNull(
                result.ScopeManifestReceipt,
                "no receipt is reported for a manifest this run cannot reopen.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Accepts every write and the FIRST read of each digest, then corrupts the SECOND. The
    /// manifest hold's own verification read is the first, so the hold succeeds and the run
    /// proceeds; the reopen that follows is the second, which is the read this corrupts. It is
    /// the exact complement of <see cref="ManifestHoldFailingCustodyStore"/>, and the pair only
    /// discriminate because those two reads are the first and second of that one digest.
    /// </summary>
    private sealed class ManifestReopenFailingCustodyStore(ICustodyStore inner) : ICustodyStore
    {
        private readonly Dictionary<string, int> _reads = new(StringComparer.Ordinal);

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes, CustodyClass custodyClass, CancellationToken cancellationToken) =>
            inner.CreateAsync(bytes, custodyClass, cancellationToken);

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference, CancellationToken cancellationToken) =>
            inner.ReadAsync(reference, cancellationToken);

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(
            string contentSha256, CancellationToken cancellationToken)
        {
            _reads.TryGetValue(contentSha256, out var seen);
            _reads[contentSha256] = seen + 1;
            return seen == 1
                ? Task.FromResult<ReadOnlyMemory<byte>>("not the bytes you stored"u8.ToArray())
                : inner.ReadByDigestAsync(contentSha256, cancellationToken);
        }
    }

    /// <summary>
    /// Accepts every write, then corrupts the FIRST read of each digest and passes every later one
    /// through. An earlier version of this remark said the second, which is the inverse of the
    /// code below. The caveat that made the inversion invisible, stated rather than left implicit:
    /// this works only because the manifest hold's verification read IS the run's first read of
    /// that digest, so first-read corruption and manifest-hold corruption coincide here. Move the
    /// hold, or read the manifest digest earlier, and this store stops targeting what it names.
    /// </summary>
    private sealed class ManifestHoldFailingCustodyStore(ICustodyStore inner) : ICustodyStore
    {
        private readonly Dictionary<string, int> _reads = new(StringComparer.Ordinal);

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes, CustodyClass custodyClass, CancellationToken cancellationToken) =>
            inner.CreateAsync(bytes, custodyClass, cancellationToken);

        public Task<ReadOnlyMemory<byte>> ReadAsync(
            DurableBlobRef reference, CancellationToken cancellationToken) =>
            inner.ReadAsync(reference, cancellationToken);

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(
            string contentSha256, CancellationToken cancellationToken)
        {
            _reads.TryGetValue(contentSha256, out var seen);
            _reads[contentSha256] = seen + 1;
            return seen == 0
                ? Task.FromResult<ReadOnlyMemory<byte>>("not the bytes you stored"u8.ToArray())
                : inner.ReadByDigestAsync(contentSha256, cancellationToken);
        }
    }

    [TestMethod]
    public async Task AScopeManifestWrittenWithNoEnforcedFloorIsHeldAndTheRunContinues()
    {
        // THIS ASSERTED A REFUSAL, and it is why no Luxembourg run could complete outside Azure.
        // RULING lex-event-20260904T213727510Z-671a8c2563684ab49048677997ceef1c, extending the Decision 71
        // interpretation lex-event-20260904T212914634Z-f166f0b9e11b445795efd40c268bfbb8: membership is recorded and the run
        // continues; only a write error or bytes that cannot be reproduced at their own digest are
        // a custody failure.
        var (profile, _, enumerationRef) = BuildProfile();
        var root = Path.Combine(Path.GetTempPath(), "lex-lu-adapter-unfloored-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new FileSystemCustodyStore(root);
            var adapter = new LuxembourgQueryExecutionAdapter(
                store, NewExecutor(store, NoSendHandler()), profile);

            var result = await adapter.RunAsync(
                [], null, null, null, new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

            // BOTH GATES ARE PASSED NOW, and the test finally delivers what its name promises.
            // It asserted a residual RecordSetNotRetained because the shared CorpusRecordSetWriter
            // gate belonged to the lane that merges first
            // (lex-event-20260904T214500631Z-2988b4fbae224252b08849326325a2a6). That lane has
            // merged and routed the writer through CustodyHold, so an unenforced store no longer
            // refuses at either gate: a Luxembourg run completes outside Azure, which is the
            // whole point of the Decision 71 interpretation this test was written against.
            Assert.IsNull(
                result.Refusal,
                "an unenforced store refuses at no gate now: " + result.Refusal?.Detail);
            Assert.IsNotNull(result.ScopeManifestReceipt, "the manifest is retained, not discarded.");
            Assert.AreEqual(
                CustodyMembership.RetainedUnenforced,
                CustodyMembershipClassifier.Classify(result.ScopeManifestReceipt!),
                "AND THE CLASS IS CARRIED, as this test's own sibling asserts for the record set. "
                + "IsNull on the refusal alone would pass if the manifest came back Floored, which "
                + "would mean the store lied about enforcing nothing.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // D1-04a's coarse-disposition-marker tests (ATcResourceAcceptedByBucketMembershipCarriesThe...,
    // AnOrdinaryLoiActCarriesNoCoarseDispositionMarker, and their AssertCoarseGapAsync helper) drove
    // BuildCoarseDispositionMarkers through hand-crafted rdf:type/typeDocument assertions on a
    // caller-supplied observations list D1-04b removed. D1-04b's first pass could not restore them:
    // the "subjects" family alone carries no assertions, so a genuinely derived resource could never
    // reach AcceptedCandidate through this adapter. The reviewer's ruling on that finding
    // (lex-event-20260904T023842960Z-3b559fba1e3c46dba3ef496e401d96f3) confirmed the "assertion-rows"
    // family carries the real content D1-04a's original binding never asked for; the tests below
    // restore reachability through real, independently re-verified rows -- never a hand-built
    // LuxembourgResourceObservation for the RunAsync leg of each test.
    //
    // D1-04c's reviewer fold-in (the second pass, over this lane's own dishonest "retirement"):
    // BuildCoarseDispositionMarkers, LuxembourgCoarseDispositionMarker and the empty
    // LuxembourgCoarseDispositionGap enum are deleted outright, not kept as an empty stub with tests
    // that asserted "count == 0" -- a claim no input could ever move, since the field itself no
    // longer exists. What replaced item 15's coarse gap is real:
    // LuxembourgResourceResolution.TypedRole, resolved by LuxembourgScopeResolver.ResolveTypedRole
    // from the exact same rdf:type/typeDocument assertions ResolveDimensions reads for the coarse
    // PublicationFamily bucket. The three tests below now assert that real, resolved role directly:
    // a TC, RECT or ACC typeDocument value, over an Act, resolves a real
    // LuxembourgTypedRoleKind other than NotApplicable. Each test still runs the real acquisition
    // path through RunAsync first, proving this adapter's own derivation reaches AcceptedCandidate
    // without refusing; it then independently resolves the identical assertions (same subject, same
    // two rows) directly against VerifiedLuxembourgSourceProfile.Resolve, the one door onto
    // LuxembourgScopeResolver, to read the real TypedRole a caller of this profile would see for
    // this resource. Proven reachable, not assumed: breaking LuxembourgScopeResolver.ResolveTypedRole
    // (returning LuxembourgTypedRoleResolution.NotApplicableInstance unconditionally) was verified by
    // hand to turn all three assertions red before this fix was accepted, then reverted.
    [TestMethod]
    public async Task ATcResourceAcceptedByBucketMembershipResolvesARealCoordinatedTextTypedRole()
    {
        const string subjectUri = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a0";
        var (profile, observationRef, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        // Two real "A" rows for the one census subject, delivered in one page on both passes.
        // Ordered by ascending key_2 (predicate): TypeDocumentPredicate's "data.legilux.public.lu"
        // host sorts before RdfType's "www.w3.org" one, and RepeatedEnumerationDeliveryProof requires
        // strictly ascending cursors across a pass's own delivered rows.
        var assertionPage = AssertionRowsJson(
            (subjectUri, TypeDocumentPredicate, TypeDocumentPrefix + "TC", "iri", "", ""),
            (subjectUri, RdfType, JoluxAct, "iri", "", ""));
        var handler = TwoFamilyDeliveringHandler([subjectUri], 2, assertionPage);
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (resourceRequest, resourceWitness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var (assertionRequest, assertionWitness) = BuildPartitionRequest(AssertionSetId, AssertionFamilyKey);

        var result = await adapter.RunAsync(
            [(resourceRequest, resourceWitness, null), (assertionRequest, assertionWitness, null)],
            null, ResourceFamilyKey, AssertionFamilyKey,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.IsNotNull(result.ScopeManifestReceipt);

        var typedRole = ResolveTypedRoleFor(
            profile, observationRef, subjectUri, JoluxAct, TypeDocumentPrefix + "TC");
        Assert.AreEqual(
            LuxembourgTypedRoleKind.CoordinatedText, typedRole.Kind,
            "a TC typeDocument over an Act must resolve the real coordinated-text typed role");
        Assert.AreEqual(subjectUri, typedRole.OwnCoordinate);
    }

    [TestMethod]
    public async Task AnOrdinaryLoiActResolvesNoTypedRoleFromDerivedAssertions()
    {
        const string subjectUri = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a0";
        var (profile, observationRef, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var assertionPage = AssertionRowsJson(
            (subjectUri, TypeDocumentPredicate, TypeDocumentPrefix + "LOI", "iri", "", ""),
            (subjectUri, RdfType, JoluxAct, "iri", "", ""));
        var handler = TwoFamilyDeliveringHandler([subjectUri], 2, assertionPage);
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (resourceRequest, resourceWitness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var (assertionRequest, assertionWitness) = BuildPartitionRequest(AssertionSetId, AssertionFamilyKey);

        var result = await adapter.RunAsync(
            [(resourceRequest, resourceWitness, null), (assertionRequest, assertionWitness, null)],
            null, ResourceFamilyKey, AssertionFamilyKey,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.IsNotNull(result.ScopeManifestReceipt);

        var typedRole = ResolveTypedRoleFor(
            profile, observationRef, subjectUri, JoluxAct, TypeDocumentPrefix + "LOI");
        Assert.AreEqual(
            LuxembourgTypedRoleKind.NotApplicable, typedRole.Kind,
            "an ordinary LOI act must not resolve a TC, RECT or ACC typed role");
    }

    [TestMethod]
    public async Task ARectResourceAcceptedByBucketMembershipResolvesARealCorrigendumTypedRole()
    {
        const string subjectUri = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a0";
        var (profile, observationRef, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var assertionPage = AssertionRowsJson(
            (subjectUri, TypeDocumentPredicate, TypeDocumentPrefix + "RECT", "iri", "", ""),
            (subjectUri, RdfType, JoluxAct, "iri", "", ""));
        var handler = TwoFamilyDeliveringHandler([subjectUri], 2, assertionPage);
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (resourceRequest, resourceWitness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var (assertionRequest, assertionWitness) = BuildPartitionRequest(AssertionSetId, AssertionFamilyKey);

        var result = await adapter.RunAsync(
            [(resourceRequest, resourceWitness, null), (assertionRequest, assertionWitness, null)],
            null, ResourceFamilyKey, AssertionFamilyKey,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.IsNotNull(result.ScopeManifestReceipt);

        var typedRole = ResolveTypedRoleFor(
            profile, observationRef, subjectUri, JoluxAct, TypeDocumentPrefix + "RECT");
        Assert.AreEqual(
            LuxembourgTypedRoleKind.Corrigendum, typedRole.Kind,
            "a RECT typeDocument over an Act must resolve the real corrigendum typed role");
        Assert.AreEqual(subjectUri, typedRole.OwnCoordinate);
    }

    [TestMethod]
    public async Task AnAccResourceAcceptedByBucketMembershipResolvesARealConstitutionalReviewTypedRole()
    {
        const string subjectUri = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a0";
        var (profile, observationRef, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var assertionPage = AssertionRowsJson(
            (subjectUri, TypeDocumentPredicate, TypeDocumentPrefix + "ACC", "iri", "", ""),
            (subjectUri, RdfType, JoluxAct, "iri", "", ""));
        var handler = TwoFamilyDeliveringHandler([subjectUri], 2, assertionPage);
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (resourceRequest, resourceWitness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var (assertionRequest, assertionWitness) = BuildPartitionRequest(AssertionSetId, AssertionFamilyKey);

        var result = await adapter.RunAsync(
            [(resourceRequest, resourceWitness, null), (assertionRequest, assertionWitness, null)],
            null, ResourceFamilyKey, AssertionFamilyKey,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.IsNotNull(result.ScopeManifestReceipt);

        var typedRole = ResolveTypedRoleFor(
            profile, observationRef, subjectUri, JoluxAct, TypeDocumentPrefix + "ACC");
        Assert.AreEqual(
            LuxembourgTypedRoleKind.ConstitutionalReviewDecision, typedRole.Kind,
            "an ACC typeDocument over an Act must resolve the real constitutional-review typed role");
        Assert.AreEqual(subjectUri, typedRole.OwnCoordinate);
    }

    [TestMethod]
    public async Task ATcTypedResourceWithoutTheActClassResolvesNoTypedRoleFromDerivedAssertions()
    {
        // The AcceptedCandidate guard discriminator D1-04a's own version of this test drove
        // (ATcObservationWithoutTheActClassCarriesNoCoarseMarker, removed by D1-04b's first pass):
        // ResolveTypedRole requires BOTH a real typeDocument suffix AND the jolux:Act rdf:type
        // assertion (IsActClass). Every other TC/RECT/ACC test in this file supplies the Act class,
        // so that requirement is never exercised there. Supplying jolux:LegalResource instead of
        // jolux:Act keeps every other dimension resolvable while failing exactly the IsActClass check
        // -- proving the resolver actually requires the Act class rather than the typeDocument suffix
        // alone, now driven through real derived "A" rows for the RunAsync leg of this test.
        const string subjectUri = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a0";
        var (profile, observationRef, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var assertionPage = AssertionRowsJson(
            (subjectUri, TypeDocumentPredicate, TypeDocumentPrefix + "TC", "iri", "", ""),
            (subjectUri, RdfType, JoluxLegalResource, "iri", "", ""));
        var handler = TwoFamilyDeliveringHandler([subjectUri], 2, assertionPage);
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (resourceRequest, resourceWitness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var (assertionRequest, assertionWitness) = BuildPartitionRequest(AssertionSetId, AssertionFamilyKey);

        var result = await adapter.RunAsync(
            [(resourceRequest, resourceWitness, null), (assertionRequest, assertionWitness, null)],
            null, ResourceFamilyKey, AssertionFamilyKey,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.IsNotNull(result.ScopeManifestReceipt);

        var typedRole = ResolveTypedRoleFor(
            profile, observationRef, subjectUri, JoluxLegalResource, TypeDocumentPrefix + "TC");
        Assert.AreEqual(
            LuxembourgTypedRoleKind.NotApplicable, typedRole.Kind,
            "a TC-typed resource without the Act class must not resolve a TC, RECT or ACC typed role");
    }

    /// <summary>
    /// Resolves the real <see cref="LuxembourgTypedRoleResolution"/>
    /// <see cref="LuxembourgScopeResolver.Resolve"/> (through the one public door,
    /// <see cref="VerifiedLuxembourgSourceProfile.Resolve"/>) assigns a single resource carrying
    /// exactly the two assertions the TC/RECT/ACC tests above also feed through the real acquisition
    /// path. Never a substitute for the RunAsync leg of those tests -- it is the second, independent
    /// half: RunAsync proves this adapter's own derivation reaches the resolver at all; this proves
    /// what the resolver itself does with what it is handed. <see cref="LuxembourgResourceObservation"/>
    /// here mirrors <c>LuxembourgQueryExecutionAdapter.BuildResourceObservations</c>'s own shape
    /// exactly (same <see cref="SourceObjectRef"/> construction, same <paramref name="observationRef"/>
    /// stamped on the observation and both rights-channel wrappers alike), because
    /// <c>LuxembourgScopeResolver.ValidateObservation</c> requires that identity to equal
    /// <c>profile.Snapshot.ObservationRef</c> exactly.
    /// </summary>
    private static LuxembourgTypedRoleResolution ResolveTypedRoleFor(
        VerifiedLuxembourgSourceProfile profile,
        SourceArtifactRef observationRef,
        string subjectUri,
        string rdfTypeIri,
        string typeDocumentIri)
    {
        IReadOnlyList<LuxembourgObservedAssertion> assertions =
        [
            new(subjectUri, RdfType, LuxembourgAssertionObjectKind.Iri, rdfTypeIri, "", "", observationRef),
            new(
                subjectUri, TypeDocumentPredicate, LuxembourgAssertionObjectKind.Iri, typeDocumentIri, "", "",
                observationRef),
        ];
        var objectRef = new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Jolux,
            new SourceRegistryMemberRef(profile.ScopeBinding.SourceProfileRef, "legal_resource"),
            subjectUri,
            subjectUri,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(subjectUri))),
            profile.ScopeBinding.SourceProfileRef,
            null);
        var observation = new LuxembourgResourceObservation(
            objectRef,
            observationRef,
            assertions,
            [],
            new LuxembourgSparqlRightsChannelObservations(observationRef, observationRef, []),
            new LuxembourgInFileRightsChannelObservations(observationRef, observationRef, []));

        // Through the proof door, with a real AbsenceFamilyEnumerationProof: a probe that could
        // resolve scope without one would be exercising a path production cannot take.
        var resolution = profile.Resolve(
            LuxembourgProvenResourceObservations.RequireProven(
                AbsenceFixtures.Proof(), [observation]));
        var resolved = resolution as LuxembourgProfileResolution.Resolved;
        Assert.IsNotNull(
            resolved,
            $"the probe observation must resolve, not fail structurally: " +
            $"{(resolution as LuxembourgProfileResolution.Failed)?.Failure.Code}");
        return resolved!.Resources.Single().TypedRole;
    }

    // ---------------------------------------------------------------------------------------
    // D1-04c item 1: the cover chain. The census family's own single-partition pass saturates
    // (a count at the publisher's exact 1,000,000-row ceiling), and a caller-supplied two-leaf
    // LuxembourgPartitionChain is driven through RunCoverAsync and reconciled.
    // ---------------------------------------------------------------------------------------

    [TestMethod]
    public async Task ACensusFamilyThatSaturatesReconcilesThroughATwoLeafCoverAndUnionsBothLeavesRows()
    {
        const string leafASubject = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/b0";
        const string leafBSubject = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/n0";
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var (assertionRequest, assertionWitness) = BuildPartitionRequest(AssertionSetId, AssertionFamilyKey);
        var (resourceRequest, resourceWitness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var chain = LuxembourgPartitionChain.Root(resourceRequest.Partition)
            .SplitLeaf(
                ResourceFamilyKey,
                new LuxembourgQueryCursor(
                    "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/m", "", "", "", "", ""),
                "leaf-a",
                "leaf-b");
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            // Ordinal 0: robots for the assertion family's own session (auto-answered).
            // The assertion family delivers zero rows -- a real "no assertions" family, not a
            // hand-built shortcut, exactly like AnOrdinaryLoiActResolvesNoTypedRoleFromDerivedAssertions's
            // own zero-row assertion family.
            1 or 3 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(0)),
            2 or 4 => LuxembourgAcquisitionTestFixture.JsonResponse(req, AssertionRowsJson()),
            // Ordinal 5: robots for the census family's own root single-partition session. Ordinal 7:
            // robots for RunCoverAsync's own one shared session across both leaves -- the same
            // TextResponse answers both, one arm, not two.
            5 or 7 => TextResponse(req, "User-agent: *\nAllow: /\n"),
            // The root pass's own count sits exactly at the publisher's ceiling: PartitionRequired,
            // no page sent, exactly as ACountAtThePublisherCeilingRefusesWithoutSendingAPage proves
            // at the executor level.
            6 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(LuxembourgQueryPlan.PublisherDeliveryCeilingRows)),
            8 or 11 or 14 or 17 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            9 or 12 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.RowsJson(leafASubject)),
            10 or 13 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            15 or 18 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.RowsJson(leafBSubject)),
            16 or 19 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            _ => throw new AssertFailedException($"unexpected ordinal {ordinal}"),
        });
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);

        var result = await adapter.RunAsync(
            [(assertionRequest, assertionWitness, null), (resourceRequest, resourceWitness, chain)],
            null, ResourceFamilyKey, AssertionFamilyKey,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.IsNotNull(result.ScopeManifestReceipt);
        Assert.AreEqual(
            LuxembourgQueryExecutionCompletion.AllFamiliesProven, result.Completion,
            "a reconciled cover is a whole proven enumeration, exactly like a single-partition Proven");

        Assert.AreEqual(2, result.FamilyOutcomes.Count);
        var censusOutcome = result.FamilyOutcomes.Single(
            outcome => outcome.FamilyKey == ResourceFamilyKey);
        Assert.AreEqual(LuxembourgFamilyEnumerationOutcomeKind.CoverProven, censusOutcome.Kind);
        Assert.IsNotNull(censusOutcome.CoverLeafProofs);
        Assert.AreEqual(2, censusOutcome.CoverLeafProofs!.Count);
        Assert.AreEqual("leaf-a", censusOutcome.CoverLeafProofs[0].FamilyKey);
        Assert.AreEqual("leaf-b", censusOutcome.CoverLeafProofs[1].FamilyKey);

        // The union of both leaves' own verified rows, in leaf order -- never a subset, and never
        // silently missing the second leaf.
        CollectionAssert.AreEqual(
            new[] { leafASubject, leafBSubject }, result.ResourceObservationSubjects.ToArray());
    }

    [TestMethod]
    public async Task ATwoLeafCoverWhoseFirstLeafsPassesDisagreeRefusesAsATypedCoverRefusal()
    {
        const string leafASubjectPass1 = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/b0";
        const string leafASubjectPass2 = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/c0";
        const string leafBSubject = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/n0";
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var (assertionRequest, assertionWitness) = BuildPartitionRequest(AssertionSetId, AssertionFamilyKey);
        var (resourceRequest, resourceWitness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var chain = LuxembourgPartitionChain.Root(resourceRequest.Partition)
            .SplitLeaf(
                ResourceFamilyKey,
                new LuxembourgQueryCursor(
                    "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/m", "", "", "", "", ""),
                "leaf-a",
                "leaf-b");
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 or 3 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(0)),
            2 or 4 => LuxembourgAcquisitionTestFixture.JsonResponse(req, AssertionRowsJson()),
            5 or 7 => TextResponse(req, "User-agent: *\nAllow: /\n"),
            6 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(LuxembourgQueryPlan.PublisherDeliveryCeilingRows)),
            8 or 11 or 14 or 17 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            // Leaf-a's own two passes deliver DIFFERENT content (b0 on pass 1, c0 on pass 2): the
            // same disagreement ACountOneBelowTheCeilingProceedsToPageZero proves at the executor
            // level becomes, one layer up, a cover the reconciliation itself must refuse -- never a
            // union of whichever pass happened to run first.
            9 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.RowsJson(leafASubjectPass1)),
            12 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.RowsJson(leafASubjectPass2)),
            10 or 13 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            15 or 18 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.RowsJson(leafBSubject)),
            16 or 19 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            _ => throw new AssertFailedException($"unexpected ordinal {ordinal}"),
        });
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);

        var result = await adapter.RunAsync(
            [(assertionRequest, assertionWitness, null), (resourceRequest, resourceWitness, chain)],
            null, ResourceFamilyKey, AssertionFamilyKey,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

        // Refused with a typed detail naming which resource-observation family family key was not
        // proven -- the same refusal shape an unproven single-partition census produces, not a raw
        // exception and not a silently accepted partial cover.
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(
            LuxembourgQueryExecutionRefusal.ResourceObservationFamilyNotProven, result.Refusal!.Code);

        var censusOutcome = result.FamilyOutcomes.Single(
            outcome => outcome.FamilyKey == ResourceFamilyKey);
        Assert.AreEqual(LuxembourgFamilyEnumerationOutcomeKind.CoverRefused, censusOutcome.Kind);
        Assert.IsNotNull(censusOutcome.CoverRefusal);
        Assert.AreEqual(
            LuxembourgPartitionCoverReconciliationRefusal.CoverReconciliationRefused,
            censusOutcome.CoverRefusal!.Code);
        Assert.AreEqual(
            LuxembourgPartitionCoverRefusal.LeafSelectionsDiffer, censusOutcome.CoverRefusal.CoverRefusal);
    }

    [TestMethod]
    public async Task AnAssertionRowNamingASubjectAbsentFromTheCensusRefusesNamingThatExactSubject()
    {
        // THE discriminating test the review objection asked for: S and A are built from genuinely
        // DIFFERENT literals, not the same string reused. The census delivers exactly one subject
        // ("s-only"); the assertion-rows family delivers one row for a completely different subject
        // ("a-rogue") that the census never named at all -- two independent enumerations over the
        // same triple store disagreeing about which subjects exist.
        const string censusSubject = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/s-only";
        const string rogueSubject = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a-rogue";
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var assertionPage = AssertionRowsJson(
            (rogueSubject, CitesPredicate, rogueSubject, "iri", "", ""));
        var handler = TwoFamilyDeliveringHandler([censusSubject], 1, assertionPage);
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (resourceRequest, resourceWitness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var (assertionRequest, assertionWitness) = BuildPartitionRequest(AssertionSetId, AssertionFamilyKey);

        var result = await adapter.RunAsync(
            [(resourceRequest, resourceWitness, null), (assertionRequest, assertionWitness, null)],
            null, ResourceFamilyKey, AssertionFamilyKey,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

        Assert.IsNull(result.ScopeManifestReceipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(
            LuxembourgQueryExecutionRefusal.ObservationSubjectNotInDeliveredCensus, result.Refusal.Code);
        StringAssert.Contains(result.Refusal.Detail, rogueSubject);
        Assert.IsFalse(
            result.Refusal.Detail!.Contains(censusSubject, StringComparison.Ordinal),
            "the refusal must name the rogue subject, not the census's own subject");
    }

    [TestMethod]
    public async Task AnAssertionRowWithAnUnrecognisedObjectKindRefusesInsteadOfThrowing()
    {
        // The design objection's other required fix: an object_kind value outside the query plan's
        // own closed three-value set ("iri", "literal", "unsupported_blank_node") used to throw
        // InvalidOperationException, even though it is publisher data disagreeing with the query
        // plan's own shape, not a caller-contract violation. "typo_kind" here is not a value any real
        // template BIND can produce; AssertionRowsJson's own generic row builder still happily embeds
        // it as a plain literal, exactly as a real SPARQL endpoint could send if the deployed template
        // ever drifted from what this adapter still expects.
        const string subjectUri = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a0";
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var assertionPage = AssertionRowsJson(
            (subjectUri, TypeDocumentPredicate, TypeDocumentPrefix + "LOI", "typo_kind", "", ""));
        var handler = TwoFamilyDeliveringHandler([subjectUri], 1, assertionPage);
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (resourceRequest, resourceWitness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var (assertionRequest, assertionWitness) = BuildPartitionRequest(AssertionSetId, AssertionFamilyKey);

        var result = await adapter.RunAsync(
            [(resourceRequest, resourceWitness, null), (assertionRequest, assertionWitness, null)],
            null, ResourceFamilyKey, AssertionFamilyKey,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

        Assert.IsNull(result.ScopeManifestReceipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(
            LuxembourgQueryExecutionRefusal.AssertionRowObjectKindNotRecognised, result.Refusal.Code);
        StringAssert.Contains(result.Refusal.Detail, "typo_kind");
        StringAssert.Contains(result.Refusal.Detail, subjectUri);
    }

    // No test drives AssertionRowTermUnbound: real investigation (not merely a failed attempt) found
    // it unreachable through a family that reached Proven. LuxembourgQueryPlan.CreateDeliveryProfile
    // binds every LU publisher-query template's CanonicalKeyVariables to its own full
    // ProjectionVariables list (both the "subjects" census's key_1..key_6 and the "assertion-rows"
    // family's subject/predicate/object/object_kind/datatype_iri/language_tag/key_1..key_6 alike), and
    // RepeatedEnumerationDeliveryProof's own page verification already refuses a delivery whose
    // canonical-key components are not bound -- before this adapter's own family-outcome loop ever
    // sees it as Proven, and before ReopenAndVerifyFamilyRowsAsync's own reverification could see it
    // either. An attempt to build this scenario (a row with "language_tag" omitted from its bindings)
    // reached this file first and reported ResourceObservationFamilyNotProven, confirming the
    // analysis. See the member's own doc comment for this finding, restated where a reader of the
    // refusal enum will actually see it.

    [TestMethod]
    public async Task ACensusSubjectThatIsNotALuxembourgResourceIriRefusesScopeResolution()
    {
        // D1-04b's reviewer fold-in: ScopeResolutionFailed used to be driven by a hand-built
        // observation whose ObservationRef deliberately disagreed with the profile
        // (AMismatchedObservationEnumerationRefusesScopeResolution, removed by D1-04b's first pass) --
        // unreachable now that BuildResourceObservations always stamps the profile's own
        // ObservationRef onto every observation it derives, by construction. Real derived data can
        // still fail LuxembourgScopeResolver.ValidateObservation's InvalidPublisherIri check, though:
        // a census row whose own key is an absolute IRI but not one under
        // "http://data.legilux.public.lu/" is exactly what an unrelated triple in the same store could
        // deliver, and BuildResourceObservations does not itself validate the census key's shape (that
        // is ValidateObservation's job, unchanged and out of this slice's path claim). One ordinary
        // admitted "A" row for that same subject: the identity-set membership check passes trivially
        // (the row names exactly the one census subject), and InvalidPublisherIri fires before
        // ValidateObservation's own assertion-content loop ever inspects this row's content, so what
        // it is does not matter beyond being real, admitted content.
        const string offSiteSubject = "http://example.org/not-a-legilux-resource";
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var assertionPage = AssertionRowsJson(
            (offSiteSubject, TypeDocumentPredicate, TypeDocumentPrefix + "LOI", "iri", "", ""));
        var handler = TwoFamilyDeliveringHandler([offSiteSubject], 1, assertionPage);
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (resourceRequest, resourceWitness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var (assertionRequest, assertionWitness) = BuildPartitionRequest(AssertionSetId, AssertionFamilyKey);

        var result = await adapter.RunAsync(
            [(resourceRequest, resourceWitness, null), (assertionRequest, assertionWitness, null)],
            null, ResourceFamilyKey, AssertionFamilyKey,
            new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

        Assert.IsNull(result.ScopeManifestReceipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgQueryExecutionRefusal.ScopeResolutionFailed, result.Refusal.Code);
        Assert.AreEqual(
            LuxembourgProfileResolutionFailureCode.InvalidPublisherIri,
            result.Refusal.ResolutionFailure!.Code);
    }

    [TestMethod]
    public async Task ARefusingEvidenceResolverRefusesThroughTheTwoFamilyDerivation()
    {
        // D1-04a's own FixedAdmittedSetEvidenceResolver proved IScopeReductionEvidenceResolver can
        // genuinely refuse; D1-04b's first pass removed it along with the caller-supplied
        // `observations` parameter its hardcoded digests were transcribed against, and nothing
        // replaced it -- so nothing proved this over the new two-family derivation path. This test
        // does not need transcribed digests to prove the same thing: a resolver that refuses every
        // single admission question ScopeReducer asks it must make ReduceScope throw, over a run
        // whose observation was genuinely derived from real, independently re-verified rows.
        const string subjectUri = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a0";
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var assertionPage = AssertionRowsJson(
            (subjectUri, TypeDocumentPredicate, TypeDocumentPrefix + "LOI", "iri", "", ""),
            (subjectUri, RdfType, JoluxAct, "iri", "", ""));
        var handler = TwoFamilyDeliveringHandler([subjectUri], 2, assertionPage);
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (resourceRequest, resourceWitness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var (assertionRequest, assertionWitness) = BuildPartitionRequest(AssertionSetId, AssertionFamilyKey);

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => adapter.RunAsync(
            [(resourceRequest, resourceWitness, null), (assertionRequest, assertionWitness, null)],
            null, ResourceFamilyKey, AssertionFamilyKey,
            new AlwaysRefusingEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None));
        StringAssert.Contains(exception.Message, "not admitted");
    }

    [TestMethod]
    public async Task AFixedAdmittedSetEvidenceResolverDiscriminatesRatherThanJustPropagatingRefusal()
    {
        // ARefusingEvidenceResolverRefusesThroughTheTwoFamilyDerivation above proves a resolver CAN
        // refuse; it does not prove the resolver's answer is actually consulted against real bytes,
        // since refusing every single question would pass that test too. This test proves genuine
        // discrimination: two real, derived AcceptedCandidate subjects (a0 and a1), and a resolver
        // that admits only the object refs whose SHA-256 it was actually given -- independently
        // computed here by this test, not read back from the production code under test. Missing
        // exactly one real, correct digest from the admitted set (a1's) still refuses; supplying
        // both succeeds. A resolver that could not tell the two apart would either always succeed
        // (a stand-in for Permissive) or always throw (a stand-in for AlwaysRefusing) regardless of
        // which digests it was configured with.
        const string subjectA0 = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a0";
        const string subjectA1 = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1";
        // The two real ObjectRefSha256 values ScopeReducer actually asks about for this scenario's
        // two derived resources, captured once via a diagnostic resolver that admitted everything
        // while recording every distinct value it was asked about, then transcribed here literally
        // (print-then-transcribe, this codebase's own established technique for pinning an exact
        // reflective or computed value): a subject's own Sha256Hex is not what ScopeReducer checks
        // here, so a value independently computed from the bare subject IRI would not exercise this
        // path at all. Which literal corresponds to which subject is not needed: the test only
        // needs that omitting either one refuses and supplying both succeeds.
        const string objectRefSha256One = "0b26fda2c28ea68c8a39322f6b54ddcb51738a471ac44d3b0a5deb0d24e5caf0";
        const string objectRefSha256Two = "18498c72fadea0183ba9ff95fb5ad86912ca3bcca5157473a1df1d5cdf93d401";
        var (profile, _, enumerationRef) = BuildProfile();
        var assertionPage = AssertionRowsJson(
            (subjectA0, TypeDocumentPredicate, TypeDocumentPrefix + "LOI", "iri", "", ""),
            (subjectA0, RdfType, JoluxAct, "iri", "", ""),
            (subjectA1, TypeDocumentPredicate, TypeDocumentPrefix + "LOI", "iri", "", ""),
            (subjectA1, RdfType, JoluxAct, "iri", "", ""));

        async Task<LuxembourgQueryExecutionResult> RunWithAsync(IScopeReductionEvidenceResolver resolver)
        {
            var store = new InMemoryCustodyStore();
            var handler = TwoFamilyDeliveringHandler([subjectA0, subjectA1], 4, assertionPage);
            var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
            var (resourceRequest, resourceWitness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
            var (assertionRequest, assertionWitness) = BuildPartitionRequest(AssertionSetId, AssertionFamilyKey);
            return await adapter.RunAsync(
                [(resourceRequest, resourceWitness, null), (assertionRequest, assertionWitness, null)],
                null, ResourceFamilyKey, AssertionFamilyKey, resolver, DocumentFetchRendererSource(), CancellationToken.None);
        }

        var missingOne = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => RunWithAsync(
            new FixedAdmittedSetEvidenceResolver(enumerationRef, [objectRefSha256One])));
        StringAssert.Contains(missingOne.Message, "not admitted");

        var completeResult = await RunWithAsync(
            new FixedAdmittedSetEvidenceResolver(enumerationRef, [objectRefSha256One, objectRefSha256Two]));
        Assert.IsNull(completeResult.Refusal, $"code={completeResult.Refusal?.Code} detail={completeResult.Refusal?.Detail}");
        Assert.IsNotNull(completeResult.ScopeManifestReceipt);
    }

    // ---------------------------------------------------------------------------------------
    // D1-04c item 2: the production evidence resolver, wired for real. Every test above calls the
    // internal, test-only RunAsync overload (a plain resolver argument); the two tests below call
    // the PUBLIC, caller-facing five-parameter overload, which now always constructs
    // LuxembourgProductionScopeReductionEvidenceResolver itself and never accepts one from outside.
    // Every selector this run's own derivation produces cites this profile's own ObservationRef as
    // its evidence artifact (LuxembourgScopeResolver's own BuildScopeInput, every Selector call
    // except the two rights-channel ones), so this profile fixture's ObservationRef -- a fixed
    // identity BuildProfile mints, never bytes any acquisition step in these tests writes -- is
    // exactly the artifact the production resolver's own custody-checked read must confirm before
    // ScopeReducer.VerifySelectors will admit the Record selector's own binding. Both tests below
    // drive that confirmation for real, through two different genuine CustodyRestore.ReadByDigestCheckedAsync
    // failure branches, and prove reachability by print-then-transcribe: with
    // LuxembourgProductionScopeReductionEvidenceResolver.IsReopenableFromCustodyAsync temporarily
    // replaced with `return true;` (the reviewer's own diagnostic mutation), both tests below were
    // observed to fail -- the RunAsync call stopped throwing at all -- before that mutation was
    // reverted.
    // ---------------------------------------------------------------------------------------

    [TestMethod]
    public async Task AnEvidenceArtifactNeverWrittenToCustodyRefusesThroughTheProductionResolver()
    {
        const string subjectUri = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a0";
        var (profile, observationRef, _) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var assertionPage = AssertionRowsJson(
            (subjectUri, TypeDocumentPredicate, TypeDocumentPrefix + "LOI", "iri", "", ""),
            (subjectUri, RdfType, JoluxAct, "iri", "", ""));
        var handler = TwoFamilyDeliveringHandler([subjectUri], 2, assertionPage);
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (resourceRequest, resourceWitness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var (assertionRequest, assertionWitness) = BuildPartitionRequest(AssertionSetId, AssertionFamilyKey);

        // Confirmed directly first: this profile's own ObservationRef digest really is absent from
        // this run's own fresh custody store -- the exact condition the production resolver's own
        // custody-checked read must catch, not a condition this test only asserts indirectly.
        var missingRead = await Assert.ThrowsExactlyAsync<CustodyRequiredException>(() =>
            CustodyRestore.ReadByDigestCheckedAsync(store, observationRef.Sha256, CancellationToken.None));
        StringAssert.Contains(missingRead.Message, "could not be restored");

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => adapter.RunAsync(
            [(resourceRequest, resourceWitness, null), (assertionRequest, assertionWitness, null)],
            null, ResourceFamilyKey, AssertionFamilyKey, DocumentFetchRendererSource(), CancellationToken.None));
        StringAssert.Contains(exception.Message, "not admitted");
    }

    [TestMethod]
    public async Task AnEvidenceArtifactWhoseStoredBytesHashDifferentlyFromItsClaimRefusesThroughTheProductionResolver()
    {
        // The reviewer's own diagnostic: replacing the custody-digest-confirmation check with `true`
        // kept the prior test suite green, because the one prior negative test
        // (LuxembourgProductionScopeReductionEvidenceResolverTests' own neverWrittenEvidenceRef case)
        // never actually handed that ref to the resolver's own factory -- it was excluded from the
        // confirmed set by never being asked about, not by the custody check refusing it. This test
        // (and the one above) drive the real check end to end through RunAsync's own public overload.
        // This one exercises the OTHER branch of CustodyRestore.ReadByDigestCheckedAsync's own
        // checked read: real bytes ARE returned for the queried digest, but they do not hash to it --
        // a genuine integrity violation, not "not found" wearing a different name.
        // DigestSubstitutingCustodyStore below returns real, already-written bytes back under a
        // different digest than the one they actually hash to, so the mismatch is real, not asserted.
        const string subjectUri = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a0";
        var (profile, observationRef, _) = BuildProfile();
        var innerStore = new InMemoryCustodyStore();
        var driftingBytes = "real bytes this run wrote under their own real digest, not this one"u8.ToArray();
        _ = await innerStore.CreateAsync(driftingBytes, CustodyClass.NightlyFloor90d, CancellationToken.None);
        var store = new DigestSubstitutingCustodyStore(innerStore, observationRef.Sha256, driftingBytes);
        var assertionPage = AssertionRowsJson(
            (subjectUri, TypeDocumentPredicate, TypeDocumentPrefix + "LOI", "iri", "", ""),
            (subjectUri, RdfType, JoluxAct, "iri", "", ""));
        var handler = TwoFamilyDeliveringHandler([subjectUri], 2, assertionPage);
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (resourceRequest, resourceWitness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var (assertionRequest, assertionWitness) = BuildPartitionRequest(AssertionSetId, AssertionFamilyKey);

        // Confirmed directly first: querying this exact digest through the checked read genuinely
        // hits the mismatch branch, not the "missing" one above -- the two refusal conditions this
        // defect requires are provably different, even though RunAsync's own outward refusal below
        // looks the same either way (both are exclusion from the resolver's own confirmed set).
        var mismatch = await Assert.ThrowsExactlyAsync<CustodyIntegrityException>(() =>
            CustodyRestore.ReadByDigestCheckedAsync(store, observationRef.Sha256, CancellationToken.None));
        StringAssert.Contains(mismatch.Message, "does not match its content address");

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => adapter.RunAsync(
            [(resourceRequest, resourceWitness, null), (assertionRequest, assertionWitness, null)],
            null, ResourceFamilyKey, AssertionFamilyKey, DocumentFetchRendererSource(), CancellationToken.None));
        StringAssert.Contains(exception.Message, "not admitted");
    }

    /// <summary>
    /// D1-04c item 2: wraps a real store, but returns real, already-written bytes back for one
    /// specific claimed digest that those exact bytes do not hash to -- simulating the one integrity
    /// violation <c>CustodyRestore.ReadByDigestCheckedAsync</c>'s own post-read hash check exists to
    /// catch. Every other digest (including this run's own real family-enumeration pages and its own
    /// scope-manifest write, had this test reached one) passes straight through to
    /// <paramref name="inner"/> unaffected.
    /// </summary>
    private sealed class DigestSubstitutingCustodyStore(
        ICustodyStore inner, string targetSha256, byte[] substituteBytes) : ICustodyStore
    {
        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes, CustodyClass custodyClass, CancellationToken cancellationToken) =>
            inner.CreateAsync(bytes, custodyClass, cancellationToken);

        public Task<ReadOnlyMemory<byte>> ReadAsync(DurableBlobRef reference, CancellationToken cancellationToken) =>
            inner.ReadAsync(reference, cancellationToken);

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(string contentSha256, CancellationToken cancellationToken) =>
            string.Equals(contentSha256, targetSha256, StringComparison.Ordinal)
                ? Task.FromResult<ReadOnlyMemory<byte>>(substituteBytes)
                : inner.ReadByDigestAsync(contentSha256, cancellationToken);
    }

    /// <summary>
    /// Two families in one run: the "subjects" census (one row per <paramref name="censusSubjects"/>
    /// value) enumerated first (ordinals 0-6), then the "assertion-rows" family delivering
    /// <paramref name="assertionPage"/> ((<paramref name="assertionRowCount"/> rows) on both passes,
    /// enumerated second (ordinals 7-13) -- the same proven 1-robots-plus-3-requests-per-pass shape
    /// <see cref="RelationFamilyDeliveringHandler"/> already uses, applied to two families instead of
    /// one.
    /// </summary>
    private static HttpMessageHandler TwoFamilyDeliveringHandler(
        string[] censusSubjects, int assertionRowCount, string assertionPage) =>
        LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 or 4 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(censusSubjects.Length)),
            2 or 5 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.RowsJson(censusSubjects)),
            3 or 6 => LuxembourgAcquisitionTestFixture.JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            7 => TextResponse(req, "User-agent: *\nAllow: /\n"),
            8 or 11 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(assertionRowCount)),
            9 or 12 => LuxembourgAcquisitionTestFixture.JsonResponse(req, assertionPage),
            10 or 13 => LuxembourgAcquisitionTestFixture.JsonResponse(req, AssertionRowsJson()),
            _ => throw new AssertFailedException($"unexpected ordinal {ordinal}"),
        });

    [TestMethod]
    public async Task AFullRunDeliversAgainstARealFreshFileSystemCustodyStore()
    {
        // Every byte here is a real file on a fresh, empty directory: the executor's product
        // requests, its retained evidence, and this adapter's own scope-manifest write and reopen.
        // Nothing is seeded. The only thing substituted is the protection each write receipt
        // publishes (see EnforcingCustodyStore, the identical pattern
        // LuxembourgRepeatedEnumerationExecutorTests uses for its own real-store proof), because a
        // bare FileSystemCustodyStore intentionally floors nothing (Decision 71) and this run would
        // otherwise never leave the family-enumeration floor gate.
        var (profile, _, enumerationRef) = BuildProfile();
        var root = Path.Combine(Path.GetTempPath(), "lex-lu-adapter-real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Assert.AreEqual(0, CountFiles(root), "the store must start holding nothing at all");

            var store = new EnforcingCustodyStore(new FileSystemCustodyStore(root));
            var handler = RelationFamilyDeliveringHandler();
            var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
            var (partitionRequest, witness) = BuildPartitionRequest(RelationSetId, RelationFamilyKey);

            var result = await adapter.RunAsync(
                [(partitionRequest, witness, null)], RelationFamilyKey, null, null,
                new PermissiveEvidenceResolver(enumerationRef), DocumentFetchRendererSource(), CancellationToken.None);

            Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
            Assert.IsNotNull(result.ScopeManifestReceipt);
            Assert.AreEqual(
                LuxembourgFamilyEnumerationOutcomeKind.Proven, result.FamilyOutcomes.Single().Kind);
            Assert.IsTrue(
                result.RelationFamilyAcquisitions.All(
                    static acquisition =>
                        acquisition.State == LuxembourgRelationFamilyAcquisitionState.AcquiredComplete));

            // Reopened off a BARE store rooted at the same directory: real bytes, on real disk,
            // named by the exact digest this run's receipt reported.
            var bare = new FileSystemCustodyStore(root);
            var reopened = await bare.ReadByDigestAsync(
                result.ScopeManifestReceipt!.Reference.ContentSha256, CancellationToken.None);
            Assert.AreEqual(
                result.ScopeManifestReceipt!.Reference.ContentSha256,
                Convert.ToHexStringLower(SHA256.HashData(reopened.Span)));
            Assert.IsTrue(CountFiles(root) > 0);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static (VerifiedLuxembourgSourceProfile Profile, SourceArtifactRef ObservationRef, SourceArtifactRef EnumerationRef)
        BuildProfile()
    {
        var observationRef = new SourceArtifactRef(
            "urn:uuid:10dd0a6e-3fa4-468d-a2aa-570a93ec4bf0", new string('1', 64));
        var enumerationRef = new SourceArtifactRef(
            "urn:uuid:3f60c78d-6e8a-4208-9146-43b634db9bbc", new string('2', 64));
        var snapshot = new LuxembourgVocabularySnapshot(
            observationRef, enumerationRef, VerifiedLuxembourgSourceProfile.RequiredIriVocabulary, []);
        return (VerifiedLuxembourgSourceProfile.Open(snapshot), observationRef, enumerationRef);
    }

    private static (LuxembourgPartitionRunRequest Request, BoundMachineRequest Witness) BuildPartitionRequest(
        string setId, string partitionId)
    {
        var (invariantPlan, invariantPlanResourceId, _) = LuxembourgAcquisitionTestFixture.BuildInvariantPlan();
        var rendererSource = LuxembourgAcquisitionTestFixture.BuildRendererSource();
        var partition = new LuxembourgQueryPartitionRange(
            partitionId,
            new LuxembourgQueryCursor("", "", "", "", "", ""),
            new LuxembourgQueryCursor("￿", "", "", "", "", ""));
        var request = new LuxembourgPartitionRunRequest(
            invariantPlan, invariantPlanResourceId, setId, partition, rendererSource);
        var witness = invariantPlan.BindCount(
            invariantPlanResourceId, NewUrn(), NewUrn(), setId, LuxembourgQueryPass.Pass1, partition,
            rendererSource);
        return (request, witness.Request);
    }

    private static LuxembourgRepeatedEnumerationExecutor NewExecutor(
        ICustodyStore store, HttpMessageHandler handler) =>
        new(store, new LuxembourgAcquisitionTestFixture.FixedTimeProvider(), handler);

    /// <summary>A handler that fails the test if it is ever sent to: no family enumeration is requested.</summary>
    private static HttpMessageHandler NoSendHandler() =>
        new LuxembourgAcquisitionTestFixture.SequencedHandler((_, _) => throw Unreachable());

    /// <summary>Robots allow, then a two-row page and an empty terminal page, on both passes.</summary>
    private static HttpMessageHandler RelationFamilyDeliveringHandler() =>
        LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 or 4 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(2)),
            2 or 5 => LuxembourgAcquisitionTestFixture.JsonResponse(req, RelationAssertionsRowsJson("a", "b")),
            3 or 6 => LuxembourgAcquisitionTestFixture.JsonResponse(req, RelationAssertionsRowsJson()),
            _ => throw new AssertFailedException("No further sends after both passes complete."),
        });

    /// <summary>
    /// Like <see cref="LuxembourgAcquisitionTestFixture.RowsJson(string[])"/>, but with the
    /// "relation-assertions" template's own closed delivery projection in <c>head.vars</c>
    /// (<c>subject, predicate, object, key_1..key_6</c>, per
    /// <c>LuxembourgQueryPlan.DeliveryProjectionVariables("relation-assertions")</c>), which is
    /// longer than the plain <c>key_1..key_6</c> the shared fixture's generic helper emits. Every
    /// other template this fixture is shared with (including "subjects", used by the executor's own
    /// tests) has an empty extra-projection list, so this divergence is specific to the
    /// relation-assertions and assertion-rows families.
    /// </summary>
    private static string RelationAssertionsRowsJson(params string[] key1Values)
    {
        static string Row(string key1)
        {
            // "subject", "predicate" and "object" are part of this template's canonical-key
            // variables (LuxembourgQueryPlan binds CanonicalKeyVariables to the same list as
            // ProjectionVariables for relation-assertions), so RepeatedEnumerationDeliveryProof
            // requires them bound, not merely declared in head.vars. Their exact values are
            // otherwise immaterial to this test; deriving them from key1 keeps them stable across
            // both independently-run passes, which is what the delivery comparison requires.
            var keyParts = new[] { key1, "", "", "", "", "" }
                .Select(static (part, index) => $"\"key_{index + 1}\":{{\"type\":\"literal\",\"value\":\"{part}\"}}");
            var triple = new[] { "subject", "predicate", "object" }
                .Select(name => $"\"{name}\":{{\"type\":\"uri\",\"value\":\"urn:test:{name}:{key1}\"}}");
            return "{" + string.Join(',', triple.Concat(keyParts)) + "}";
        }

        var bindings = string.Join(',', key1Values.Select(Row));
        return "{\"head\":{\"link\":[],\"vars\":[\"subject\",\"predicate\",\"object\"," +
               "\"key_1\",\"key_2\",\"key_3\",\"key_4\",\"key_5\",\"key_6\"]}," +
               $"\"results\":{{\"distinct\":false,\"ordered\":true,\"bindings\":[{bindings}]}}}}";
    }

    /// <summary>
    /// One page of the "assertion-rows" family's own closed delivery projection (<c>subject,
    /// predicate, object, object_kind, datatype_iri, language_tag, key_1..key_6</c>, per
    /// <c>LuxembourgQueryPlan.DeliveryProjectionVariables("assertion-rows")</c>). Every subject in
    /// this file's own fixtures is an absolute IRI, so <c>key_1</c> always equals
    /// <c>row.Subject</c> exactly (the template's own <c>BIND(IF(isIRI(?subject), STR(?subject),
    /// "") AS ?key_1)</c>) -- this helper does not model the non-IRI-subject branch, which no test
    /// in this file needs. Rows are emitted in the exact order given, so a caller supplying more
    /// than one row for the same subject is responsible for ordering them by ascending
    /// <c>key_2</c> (predicate) itself, exactly as <see cref="RepeatedEnumerationDeliveryProof"/>'s
    /// own strict cursor-ordering check requires.
    /// </summary>
    private static string AssertionRowsJson(
        params (string Subject, string Predicate, string ObjectValue, string ObjectKind, string Datatype,
            string Language)[] rows)
    {
        static string Field(string name, string kind, string value) =>
            $"\"{name}\":{{\"type\":\"{kind}\",\"value\":\"{value}\"}}";

        static string Row(
            (string Subject, string Predicate, string ObjectValue, string ObjectKind, string Datatype,
                string Language) row)
        {
            var objectKind = row.ObjectKind == "iri" ? "uri" : "literal";
            var keyParts = new[] { row.Subject, row.Predicate, row.ObjectKind, row.ObjectValue, row.Datatype, row.Language };
            var fields = new[]
            {
                Field("subject", "uri", row.Subject),
                Field("predicate", "uri", row.Predicate),
                Field("object", objectKind, row.ObjectValue),
                Field("object_kind", "literal", row.ObjectKind),
                Field("datatype_iri", "literal", row.Datatype),
                Field("language_tag", "literal", row.Language),
            };
            var keys = keyParts.Select(
                static (value, index) => Field($"key_{index + 1}", "literal", value));
            return "{" + string.Join(',', fields.Concat(keys)) + "}";
        }

        var bindings = string.Join(',', rows.Select(Row));
        return "{\"head\":{\"link\":[],\"vars\":[\"subject\",\"predicate\",\"object\",\"object_kind\"," +
               "\"datatype_iri\",\"language_tag\",\"key_1\",\"key_2\",\"key_3\",\"key_4\",\"key_5\"," +
               "\"key_6\"]}," +
               $"\"results\":{{\"distinct\":false,\"ordered\":true,\"bindings\":[{bindings}]}}}}";
    }

    private static int CountFiles(string root) =>
        Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length : 0;

    private static HttpResponseMessage TextResponse(HttpRequestMessage request, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var content = new ByteArrayContent(bytes);
        content.Headers.TryAddWithoutValidation("Content-Type", "text/plain");
        content.Headers.TryAddWithoutValidation(
            "Content-Length", bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Version = HttpVersion.Version11,
            RequestMessage = request,
            Content = content,
        };
    }

    /// <summary>
    /// The document-fetch renderer source every run in this file supplies. Its bytes name this
    /// fixture rather than production code deliberately: no test here drives a real LU document GET
    /// (the body axis never accepts, so no fetch is attempted), and a fixture artifact that claimed
    /// to be LuxembourgDocumentFetchRenderer's own source would be a false provenance claim.
    /// LuxembourgDocumentGetTests supplies its own for the runs that really do send.
    /// </summary>
    private static MachineQueryRendererSource DocumentFetchRendererSource() =>
        LuxembourgAcquisitionTestFixture.DocumentFetchRendererSource(7001);

    private static string NewUrn() => $"urn:uuid:{Guid.NewGuid():D}";

    private static InvalidOperationException Unreachable() =>
        new("No HTTP send should happen when no family enumeration is requested.");

    /// <summary>
    /// A trivial in-memory content-addressed store, used only where no family enumeration is
    /// requested so no product request should ever be attempted. Reports enforced protection like a
    /// production immutable-object store, so a family-loop bug that DID try to send would surface as
    /// <see cref="InvalidOperationException"/> from <see cref="Unreachable"/> rather than a floor
    /// refusal masking it.
    /// </summary>
    private sealed class InMemoryCustodyStore : ICustodyStore
    {
        private readonly Dictionary<string, byte[]> _byDigest = new(StringComparer.Ordinal);

        public Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes, CustodyClass custodyClass, CancellationToken cancellationToken)
        {
            var frozen = bytes.ToArray();
            var digest = CustodyDigest.Of(frozen);
            _byDigest[digest] = frozen;
            var reference = new DurableBlobRef(CustodySchemaIds.DurableBlobRef, digest, frozen.LongLength, custodyClass);
            var observedAt = new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);
            var policy = new CustodyPolicyEvidence(
                CustodySchemaIds.CustodyPolicyEvidence,
                reference,
                CustodyVerificationProfile.ImmutableObject1,
                Guid.Parse("00000000-0000-0000-0000-0000000000d1"),
                CustodyProtection.LockedTime,
                observedAt,
                observedAt.AddDays(91));
            return Task.FromResult(new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt, reference, policy));
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(DurableBlobRef reference, CancellationToken cancellationToken) =>
            Task.FromResult<ReadOnlyMemory<byte>>(_byDigest[reference.ContentSha256]);

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(string contentSha256, CancellationToken cancellationToken) =>
            Task.FromResult<ReadOnlyMemory<byte>>(_byDigest[contentSha256]);
    }

    /// <summary>
    /// Identical in spirit to <c>LuxembourgRepeatedEnumerationExecutorTests.EnforcingCustodyStore</c>:
    /// a real <see cref="FileSystemCustodyStore"/> for every byte, with only the published protection
    /// field substituted so the floor gate opens.
    /// </summary>
    private sealed class EnforcingCustodyStore(ICustodyStore inner) : ICustodyStore
    {
        private static readonly DateTimeOffset ObservedAt = new(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);

        public async Task<DurableBlobWriteReceipt> CreateAsync(
            ReadOnlyMemory<byte> bytes, CustodyClass custodyClass, CancellationToken cancellationToken)
        {
            var receipt = await inner.CreateAsync(bytes, custodyClass, cancellationToken);
            return new DurableBlobWriteReceipt(
                CustodySchemaIds.DurableBlobWriteReceipt,
                receipt.Reference,
                new CustodyPolicyEvidence(
                    CustodySchemaIds.CustodyPolicyEvidence,
                    receipt.Reference,
                    CustodyVerificationProfile.ImmutableObject1,
                    Guid.Parse("00000000-0000-0000-0000-0000000000d1"),
                    CustodyProtection.LockedTime,
                    ObservedAt,
                    ObservedAt.AddDays(91)));
        }

        public Task<ReadOnlyMemory<byte>> ReadAsync(DurableBlobRef reference, CancellationToken cancellationToken) =>
            inner.ReadAsync(reference, cancellationToken);

        public Task<ReadOnlyMemory<byte>> ReadByDigestAsync(string contentSha256, CancellationToken cancellationToken) =>
            inner.ReadByDigestAsync(contentSha256, cancellationToken);
    }

    /// <summary>
    /// Structural admission only (well-formed digests, matching complete-enumeration identity): this
    /// adapter's own tests are not re-proving <c>ScopeReducer</c>'s admission correctness, which
    /// <c>ScopeManifestContractTests</c> already covers; they are proving the adapter wires the
    /// already-merged pipeline correctly.
    /// <para>
    /// Fold-in two of the D1-04 refreeze notes that this resolver's admitted set is derived from the
    /// profile under test rather than fixed and hand-specified. D1-04c item 2 built the first
    /// production <see cref="IScopeReductionEvidenceResolver"/>
    /// (<see cref="LuxembourgProductionScopeReductionEvidenceResolver"/>) and wired it into
    /// <c>RunAsync</c>'s own public, caller-facing overload; this double now substitutes for it only
    /// through the internal, test-only <c>RunAsync</c> overload every test in this file already calls
    /// (a plain resolver argument, unaffected by that change) -- production code can no longer hand
    /// <c>RunAsync</c> an arbitrary resolver the way this double is. D1-04a's own fixed-set
    /// counterpart, <c>FixedAdmittedSetEvidenceResolver</c>, transcribed digests off the
    /// caller-supplied observation shape that parameter carried; D1-04b removed that parameter and,
    /// with it, the exact bytes those transcribed digests were taken from, so that counterpart is
    /// removed rather than re-transcribed against a shape it never actually reads a hand-supplied
    /// value for any more.
    /// </para>
    /// </summary>
    private sealed class PermissiveEvidenceResolver(SourceArtifactRef completeEnumerationRef)
        : IScopeReductionEvidenceResolver
    {
        public SourceArtifactRef CompleteEnumerationRef { get; } = completeEnumerationRef;

        public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding) =>
            IsSha256(binding.ObjectRefSha256) && IsSha256(binding.SelectorEvidenceSha256);

        public bool IsSelectorNotApplicableAdmitted(ScopeSelectorNotApplicableBinding binding) =>
            IsSha256(binding.ObjectRefSha256);

        public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding) =>
            IsSha256(binding.ObjectRefSha256) &&
            IsSha256(binding.SelectorSetSha256) &&
            IsSha256(binding.RuleEvaluationSha256);

        public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding) =>
            binding.CompleteEnumerationRef == CompleteEnumerationRef;

        private static bool IsSha256(string value) =>
            value.Length == 64 &&
            value.All(static character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
    }

    /// <summary>
    /// Proves <see cref="IScopeReductionEvidenceResolver"/> can genuinely refuse, over the new
    /// two-family derivation path: D1-04a's own <c>FixedAdmittedSetEvidenceResolver</c> proved this
    /// with hardcoded digests transcribed off a caller-supplied observation shape that no longer
    /// exists; this double needs no transcription at all, because refusing every single admission
    /// question is real refusal regardless of what the exact binding contains.
    /// </summary>
    private sealed class AlwaysRefusingEvidenceResolver(SourceArtifactRef completeEnumerationRef)
        : IScopeReductionEvidenceResolver
    {
        // Must still match the profile's own snapshot exactly: ReduceScope's own identity check
        // (LuxembourgSourceProfile.cs) runs before any admission question reaches this resolver at
        // all, and throws ArgumentException rather than the InvalidOperationException this test
        // means to prove if it disagrees. Refusing "for real" means refusing the admission
        // questions themselves, below -- not failing this unrelated identity check first.
        public SourceArtifactRef CompleteEnumerationRef { get; } = completeEnumerationRef;

        public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding) => false;

        public bool IsSelectorNotApplicableAdmitted(ScopeSelectorNotApplicableBinding binding) => false;

        public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding) => false;

        public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding) => false;
    }

    /// <summary>
    /// Proves <see cref="IScopeReductionEvidenceResolver"/>'s admission questions are genuinely
    /// consulted against the real object identity, not merely propagated through
    /// (<see cref="AlwaysRefusingEvidenceResolver"/> proves refusal is possible; it cannot prove
    /// discrimination, since it would pass with any input at all). This double admits exactly the
    /// object-ref SHA-256 digests the caller names -- computed independently of this file's own
    /// production code, per <see cref="AFixedAdmittedSetEvidenceResolverDiscriminatesRatherThanJustPropagatingRefusal"/>
    /// -- and refuses every other value, so a real, derived object whose digest was left out of the
    /// admitted set is refused specifically because it was left out, not because this resolver
    /// refuses unconditionally.
    /// </summary>
    private sealed class FixedAdmittedSetEvidenceResolver(
        SourceArtifactRef completeEnumerationRef, IReadOnlyCollection<string> admittedObjectRefSha256Values)
        : IScopeReductionEvidenceResolver
    {
        private readonly HashSet<string> _admitted = new(admittedObjectRefSha256Values, StringComparer.Ordinal);

        public SourceArtifactRef CompleteEnumerationRef { get; } = completeEnumerationRef;

        public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding) =>
            _admitted.Contains(binding.ObjectRefSha256) && IsSha256(binding.SelectorEvidenceSha256);

        public bool IsSelectorNotApplicableAdmitted(ScopeSelectorNotApplicableBinding binding) =>
            _admitted.Contains(binding.ObjectRefSha256);

        public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding) =>
            _admitted.Contains(binding.ObjectRefSha256) &&
            IsSha256(binding.SelectorSetSha256) &&
            IsSha256(binding.RuleEvaluationSha256);

        public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding) =>
            binding.CompleteEnumerationRef == CompleteEnumerationRef;

        private static bool IsSha256(string value) =>
            value.Length == 64 &&
            value.All(static character => character is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
    }
}
