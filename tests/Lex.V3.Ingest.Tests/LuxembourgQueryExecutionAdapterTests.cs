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
    private const string JoluxAct = "http://data.legilux.public.lu/resource/ontology/jolux#Act";
    private const string JoluxLegalResource =
        "http://data.legilux.public.lu/resource/ontology/jolux#LegalResource";
    private const string RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";
    private const string TypeDocumentPredicate =
        "http://data.legilux.public.lu/resource/ontology/jolux#typeDocument";
    private const string TypeDocumentPrefix =
        "http://data.legilux.public.lu/resource/authority/resource-type/";

    [TestMethod]
    public async Task TopologyIsAlwaysMintedEvenOnRefusal()
    {
        var (profile, observationRef, enumerationRef) = BuildProfile();
        var adapter = new LuxembourgQueryExecutionAdapter(
            new InMemoryCustodyStore(), NewExecutor(new InMemoryCustodyStore(), NoSendHandler()),
            profile);

        var result = await adapter.RunAsync(
            [], null, null, [], new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

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
        _ = observationRef;
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
                LuxembourgQueryExecutionRefusal.ScopeManifestNotHeld, failure, null));
        Assert.ThrowsExactly<ArgumentException>(static () =>
            new LuxembourgQueryExecutionRefusalDetail(
                LuxembourgQueryExecutionRefusal.ScopeResolutionFailed, null, null));
    }

    [TestMethod]
    public async Task NoRelationFamilyDesignatedMeansUnacquired()
    {
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var adapter = new LuxembourgQueryExecutionAdapter(
            store, NewExecutor(store, NoSendHandler()), profile);

        var result = await adapter.RunAsync(
            [], null, null, [], new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

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
            [], RelationFamilyKey, null, [], new PermissiveEvidenceResolver(enumerationRef),
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
            [(partitionRequest, witness)], RelationFamilyKey, null, [],
            new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

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
            [(partitionRequest, witness)], RelationFamilyKey, null, [],
            new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

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
            [(partitionRequest, witness)], RelationFamilyKey, null, [],
            new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

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
            [(partitionRequest, witness)], null, null, [],
            new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.IsNotNull(result.ScopeManifestReceipt, "a refused family does not stop the manifest write");
        Assert.AreEqual(LuxembourgFamilyEnumerationOutcomeKind.ProofRefused, result.FamilyOutcomes.Single().Kind);
        Assert.AreEqual(LuxembourgQueryExecutionCompletion.PartialFamilyRefused, result.Completion);
    }

    [TestMethod]
    public async Task ObservationsWithNoDesignatedResourceFamilyAreRefused()
    {
        // THE OBJECTION this refreeze closes, first branch: a hand-built observation with no
        // enumeration at all behind it. Before this refreeze this delivered a manifest from a
        // family this run never enumerated; now it refuses.
        var (profile, observationRef, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var adapter = new LuxembourgQueryExecutionAdapter(
            store, NewExecutor(store, NoSendHandler()), profile);
        var observation = Observation(
            observationRef,
            "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1",
            TypeDocumentPrefix + "LOI");

        var result = await adapter.RunAsync(
            [], null, null, [observation], new PermissiveEvidenceResolver(enumerationRef),
            CancellationToken.None);

        Assert.IsNull(result.ScopeManifestReceipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgQueryExecutionRefusal.ObservationsWithoutProvenCensus, result.Refusal.Code);
    }

    [TestMethod]
    public async Task ADesignatedResourceFamilyNeverEnumeratedRefusesTheObservations()
    {
        // Second branch: a resource family IS named, but this run's family list never actually
        // enumerated it (a caller/config mismatch, exactly as
        // ADesignatedFamilyKeyNeverRequestedIsUncertain covers for the relation family -- except
        // here observations are actually at stake, so the honest answer is a hard refusal, not an
        // "uncertain" acquisition state).
        var (profile, observationRef, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var adapter = new LuxembourgQueryExecutionAdapter(
            store, NewExecutor(store, NoSendHandler()), profile);
        var observation = Observation(
            observationRef,
            "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1",
            TypeDocumentPrefix + "LOI");

        var result = await adapter.RunAsync(
            [], null, ResourceFamilyKey, [observation], new PermissiveEvidenceResolver(enumerationRef),
            CancellationToken.None);

        Assert.IsNull(result.ScopeManifestReceipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgQueryExecutionRefusal.ObservationsWithoutProvenCensus, result.Refusal.Code);
    }

    [TestMethod]
    public async Task AMismatchedObservationCountRefusesTheCensus()
    {
        // THE required mismatch test: the resource family is genuinely proven, and it genuinely
        // delivered two rows, but only one observation is supplied. Count is the binding this
        // refreeze can actually make (see the resourceObservationFamilyKey remark on RunAsync for
        // why this is not full identity-set equality); this is the test that proves it is real.
        var (profile, observationRef, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var handler = ResourceFamilyDeliveringHandler(rowCount: 2);
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (partitionRequest, witness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var observation = Observation(
            observationRef,
            "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1",
            TypeDocumentPrefix + "LOI");

        var result = await adapter.RunAsync(
            [(partitionRequest, witness)], null, ResourceFamilyKey, [observation],
            new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

        Assert.IsNull(result.ScopeManifestReceipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(
            LuxembourgQueryExecutionRefusal.ObservationCountDoesNotMatchDelivery, result.Refusal.Code);
        StringAssert.Contains(result.Refusal.Detail, "1 observation");
        StringAssert.Contains(result.Refusal.Detail, "2");
    }

    [TestMethod]
    public async Task ADesignatedResourceFamilyThatIsFoundButNotProvenRefusesTheObservations()
    {
        // The third way the census guard can fail, distinct from "never enumerated": the family key
        // IS found among this run's outcomes, but its enumeration did not prove complete (mirroring
        // AMismatchedSelectionProducesAProofRefusedOutcome's own handler). A guard that only checked
        // "was this key found" and not "was it Proven" would let a caller attach observations to a
        // family whose census this run never actually finished.
        var (profile, observationRef, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 or 3 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(999_999)),
            _ => LuxembourgAcquisitionTestFixture.JsonResponse(req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
        });
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (partitionRequest, witness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var observation = Observation(
            observationRef,
            "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1",
            TypeDocumentPrefix + "LOI");

        var result = await adapter.RunAsync(
            [(partitionRequest, witness)], null, ResourceFamilyKey, [observation],
            new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

        Assert.AreEqual(LuxembourgFamilyEnumerationOutcomeKind.ProofRefused, result.FamilyOutcomes.Single().Kind);
        Assert.IsNull(result.ScopeManifestReceipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgQueryExecutionRefusal.ObservationsWithoutProvenCensus, result.Refusal.Code);
    }

    [TestMethod]
    public async Task TheCensusGuardMatchesTheDesignatedFamilyByKeyNotByPosition()
    {
        // Two families enumerated in one run, both Proven, the designated resource family listed
        // SECOND and delivering a DIFFERENT row count (1) than the first, unrelated family (2). A
        // guard that matched "the first Proven outcome" or "any Proven outcome" rather than the
        // outcome whose FamilyKey equals resourceObservationFamilyKey would compare this test's one
        // observation against the relation family's count of 2, not the resource family's count of
        // 1, and refuse ObservationCountDoesNotMatchDelivery instead of succeeding.
        //
        // Each family is its own RoutedHttpAcquisitionSession (LuxembourgRepeatedEnumerationExecutor
        // .RunPartitionAsync starts a fresh one every call), and SequencedHandler's ordinal counter
        // is one running total across the whole shared handler instance, so this handler answers
        // robots twice -- once at ordinal 0 for the relation family's own bootstrap, once at ordinal
        // 7 for the resource family's.
        var (profile, observationRef, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var (relationRequest, relationWitness) = BuildPartitionRequest(RelationSetId, RelationFamilyKey);
        var (resourceRequest, resourceWitness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var observation = Observation(
            observationRef,
            "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1",
            TypeDocumentPrefix + "LOI");
        var handler = LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            7 => TextResponse(req, "User-agent: *\nAllow: /\n"),
            1 or 4 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(2)),
            2 or 5 => LuxembourgAcquisitionTestFixture.JsonResponse(req, RelationAssertionsRowsJson("a", "b")),
            3 or 6 => LuxembourgAcquisitionTestFixture.JsonResponse(req, RelationAssertionsRowsJson()),
            8 or 11 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(1)),
            9 or 12 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.RowsJson("a")),
            10 or 13 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            _ => throw new AssertFailedException($"unexpected ordinal {ordinal}"),
        });
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);

        var result = await adapter.RunAsync(
            [(relationRequest, relationWitness), (resourceRequest, resourceWitness)],
            RelationFamilyKey, ResourceFamilyKey, [observation],
            new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

        Assert.AreEqual(2, result.FamilyOutcomes.Count);
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
    public async Task AMatchingResourceCensusLetsScopeResolutionProceed()
    {
        // The positive control for the same guard: one delivered row, one matching observation,
        // designated correctly -- the run proceeds exactly as it did before this refreeze existed.
        var (profile, observationRef, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var handler = ResourceFamilyDeliveringHandler(rowCount: 1);
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (partitionRequest, witness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var observation = Observation(
            observationRef,
            "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1",
            TypeDocumentPrefix + "LOI");

        var result = await adapter.RunAsync(
            [(partitionRequest, witness)], null, ResourceFamilyKey, [observation],
            new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.IsNotNull(result.ScopeManifestReceipt);
        Assert.AreEqual(LuxembourgQueryExecutionCompletion.AllFamiliesProven, result.Completion);
    }

    [TestMethod]
    public async Task AFixedAdmittedSetResolverProducesTheSameDeliveredResultAsThePermissiveOne()
    {
        // Fold-in two: FixedAdmittedSetEvidenceResolver's digests are transcribed literals, printed
        // from a throwaway failing assertion against this exact scenario (BuildProfile plus one
        // ordinary LOI observation over a one-row resource-observation family), not derived from
        // the profile under test the way PermissiveEvidenceResolver's structural checks are. Using
        // it in place of the permissive resolver and reaching the identical delivered outcome proves
        // the adapter's wiring reads exactly what a resolver admits.
        var (profile, observationRef, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var handler = ResourceFamilyDeliveringHandler(rowCount: 1);
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (partitionRequest, witness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var observation = Observation(
            observationRef,
            "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1",
            TypeDocumentPrefix + "LOI");

        var result = await adapter.RunAsync(
            [(partitionRequest, witness)], null, ResourceFamilyKey, [observation],
            new FixedAdmittedSetEvidenceResolver(enumerationRef), CancellationToken.None);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.IsNotNull(result.ScopeManifestReceipt);
        Assert.AreEqual(0, result.CoarseDispositionMarkers.Count);
        Assert.AreEqual(LuxembourgQueryExecutionCompletion.AllFamiliesProven, result.Completion);
    }

    [TestMethod]
    public async Task AFixedAdmittedSetResolverRefusesAnUntranscribedObjectRef()
    {
        // The other half of "actually discriminates": change the one input that changes every
        // digest FixedAdmittedSetEvidenceResolver checks -- the observed publisher URI, which feeds
        // SourceObjectRef's own content hash -- and the fixed resolver must refuse admission instead
        // of silently passing a shape it was never given. ScopeReducer.VerifySelectors treats a
        // refused selector-observation binding as a hard InvalidOperationException, not a typed
        // LuxembourgQueryExecutionRefusal (that pipeline behavior is the merged R5.1 reducer's own,
        // not this adapter's); what this test pins is that the fixed resolver's refusal is real
        // enough to reach it, proving the resolver discriminates rather than always admitting.
        var (profile, observationRef, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var handler = ResourceFamilyDeliveringHandler(rowCount: 1);
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (partitionRequest, witness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var observation = Observation(
            observationRef,
            "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a2-different",
            TypeDocumentPrefix + "LOI");

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => adapter.RunAsync(
            [(partitionRequest, witness)], null, ResourceFamilyKey, [observation],
            new FixedAdmittedSetEvidenceResolver(enumerationRef), CancellationToken.None));
        StringAssert.Contains(exception.Message, "not admitted");
    }

    [TestMethod]
    public async Task AMismatchedObservationEnumerationRefusesScopeResolution()
    {
        var (profile, observationRef, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var handler = ResourceFamilyDeliveringHandler(rowCount: 1);
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (partitionRequest, witness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var wrongRef = new SourceArtifactRef(
            "urn:uuid:00000000-0000-4000-8000-0000000000ee", new string('9', 64));
        var badObservation = Observation(
            observationRef: wrongRef,
            publisherUri: "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1",
            typeDocumentIri: TypeDocumentPrefix + "LOI");
        _ = observationRef;

        // The census guard is satisfied (one delivered row, one observation), so this run reaches
        // _sourceProfile.Resolve, which is what actually refuses here: the observation's own
        // ObservationRef does not bind this profile's enumeration artifact. That is a different
        // failure from the census guard above and stays covered separately.
        var result = await adapter.RunAsync(
            [(partitionRequest, witness)], null, ResourceFamilyKey, [badObservation],
            new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

        Assert.IsNull(result.ScopeManifestReceipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgQueryExecutionRefusal.ScopeResolutionFailed, result.Refusal.Code);
        Assert.AreEqual(
            LuxembourgProfileResolutionFailureCode.EvidenceBindingRejected,
            result.Refusal.ResolutionFailure!.Code);
    }

    [TestMethod]
    public async Task AScopeManifestWrittenWithNoEnforcedFloorIsRefused()
    {
        // A bare FileSystemCustodyStore publishes NotEnforced for every write (Decision 71), so the
        // manifest this run produces cannot be claimed as held evidence -- exactly the discipline
        // RequireFlooredRun applies to the executor's own evidence, applied here to this adapter's.
        var (profile, _, enumerationRef) = BuildProfile();
        var root = Path.Combine(Path.GetTempPath(), "lex-lu-adapter-unfloored-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new FileSystemCustodyStore(root);
            var adapter = new LuxembourgQueryExecutionAdapter(
                store, NewExecutor(store, NoSendHandler()), profile);

            var result = await adapter.RunAsync(
                [], null, null, [], new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

            Assert.IsNull(result.ScopeManifestReceipt);
            Assert.IsNotNull(result.Refusal);
            Assert.AreEqual(LuxembourgQueryExecutionRefusal.ScopeManifestNotHeld, result.Refusal.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ATcResourceAcceptedByBucketMembershipCarriesTheTypedGapMarker() =>
        await AssertCoarseGapAsync("TC", LuxembourgCoarseDispositionGap.TcTypedRoleNotDistinguished);

    [TestMethod]
    public async Task ARectResourceAcceptedByBucketMembershipCarriesTheTypedGapMarker() =>
        await AssertCoarseGapAsync("RECT", LuxembourgCoarseDispositionGap.RectTypedRoleNotDistinguished);

    [TestMethod]
    public async Task AnAccResourceAcceptedByBucketMembershipCarriesTheTypedGapMarker() =>
        await AssertCoarseGapAsync(
            "ACC", LuxembourgCoarseDispositionGap.AccTypedRoleNotDistinguished);

    [TestMethod]
    public async Task ATcObservationWithoutTheActClassCarriesNoCoarseMarker()
    {
        // Fold-in five: drives the AcceptedCandidate guard in BuildCoarseDispositionMarkers with a
        // TC-typed object that is genuinely NOT an AcceptedCandidate disposition, proving the guard
        // actually discriminates rather than always passing. Every other TC/RECT/ACC test in this
        // file supplies the jolux:Act rdf:type assertion IsActClass requires, so
        // LuxembourgScopeResolver.ResolvePublicationFamily always lands on AcceptedCandidate and the
        // guard's "!= AcceptedCandidate" branch was never taken. Supplying jolux:LegalResource
        // instead of jolux:Act keeps every other dimension resolvable while failing exactly the
        // IsActClass check the priority-candidate bucket also requires, landing the resource on
        // TypedQuarantine instead.
        var (profile, observationRef, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var handler = ResourceFamilyDeliveringHandler(rowCount: 1);
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (partitionRequest, witness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        const string publisherUri = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1";
        var observation = Observation(
            observationRef, publisherUri, TypeDocumentPrefix + "TC", rdfTypeIri: JoluxLegalResource);

        var result = await adapter.RunAsync(
            [(partitionRequest, witness)], null, ResourceFamilyKey, [observation],
            new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

        Assert.IsNotNull(result.ScopeManifestReceipt, $"refusal={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.AreEqual(
            0, result.CoarseDispositionMarkers.Count,
            "a TC resource without the Act class must not resolve to AcceptedCandidate, so the guard " +
            "must skip it");
    }

    [TestMethod]
    public async Task AnOrdinaryLoiActCarriesNoCoarseDispositionMarker()
    {
        var (profile, observationRef, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var handler = ResourceFamilyDeliveringHandler(rowCount: 1);
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (partitionRequest, witness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        var observation = Observation(
            observationRef,
            "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1",
            TypeDocumentPrefix + "LOI");

        var result = await adapter.RunAsync(
            [(partitionRequest, witness)], null, ResourceFamilyKey, [observation],
            new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

        Assert.IsNotNull(result.ScopeManifestReceipt, $"refusal={result.Refusal?.Code}");
        Assert.AreEqual(0, result.CoarseDispositionMarkers.Count);
    }

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
                [(partitionRequest, witness)], RelationFamilyKey, null, [],
                new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

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

    private async Task AssertCoarseGapAsync(string typeDocumentSuffix, LuxembourgCoarseDispositionGap expectedGap)
    {
        var (profile, observationRef, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var handler = ResourceFamilyDeliveringHandler(rowCount: 1);
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (partitionRequest, witness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
        const string publisherUri = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1";
        var observation = Observation(observationRef, publisherUri, TypeDocumentPrefix + typeDocumentSuffix);

        var result = await adapter.RunAsync(
            [(partitionRequest, witness)], null, ResourceFamilyKey, [observation],
            new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

        Assert.IsNotNull(result.ScopeManifestReceipt, $"refusal={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.AreEqual(1, result.CoarseDispositionMarkers.Count);
        var marker = result.CoarseDispositionMarkers[0];
        Assert.AreEqual(publisherUri, marker.PublisherUri);
        Assert.AreEqual(TypeDocumentPrefix + typeDocumentSuffix, marker.ObservedTypeDocumentIri);
        Assert.AreEqual(expectedGap, marker.Gap);
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

    private static LuxembourgResourceObservation Observation(
        SourceArtifactRef observationRef, string publisherUri, string typeDocumentIri,
        string rdfTypeIri = JoluxAct)
    {
        var objectRef = new SourceObjectRef(
            SourceCoreSchemaIds.SourceObjectRef,
            SourceAuthority.Jolux,
            new SourceRegistryMemberRef(
                new SourceArtifactRef(
                    "urn:uuid:760b560c-15c2-407d-b38f-f99f4c59e345", new string('3', 64)),
                "legal_resource"),
            publisherUri,
            publisherUri,
            Sha256(publisherUri),
            new SourceArtifactRef("urn:uuid:54b9c06f-ed04-4d07-8239-72dce5fed499", new string('4', 64)),
            null);
        IReadOnlyList<LuxembourgObservedAssertion> assertions =
        [
            new LuxembourgObservedAssertion(
                publisherUri, RdfType, LuxembourgAssertionObjectKind.Iri, rdfTypeIri, "", "",
                observationRef),
            new LuxembourgObservedAssertion(
                publisherUri, TypeDocumentPredicate, LuxembourgAssertionObjectKind.Iri, typeDocumentIri, "", "",
                observationRef),
        ];
        return new LuxembourgResourceObservation(
            objectRef,
            observationRef,
            assertions,
            [],
            new LuxembourgSparqlRightsChannelObservations(
                observationRef,
                new SourceArtifactRef(
                    "urn:uuid:8b42bff0-128c-4daa-a111-d05452d9b0c8", new string('5', 64)),
                []),
            new LuxembourgInFileRightsChannelObservations(
                observationRef,
                new SourceArtifactRef(
                    "urn:uuid:90a12718-936e-4e43-9be7-d1ee407cf9b5", new string('6', 64)),
                []));
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
    /// Robots allow, then a <paramref name="rowCount"/>-row page and an empty terminal page, on
    /// both passes, using the plain "subjects" template's own key_1..key_6 projection (no
    /// relation-assertions style extra columns): the same generic shape D1-03's own executor tests
    /// use for a resource family. Row keys are the first <paramref name="rowCount"/> lowercase
    /// letters, which this file's own <see cref="BuildPartitionRequest"/> range ("" to "￿") always
    /// contains. Requires <paramref name="rowCount"/> to be at least 1 and small enough to fit in
    /// one page (every scenario in this file uses 1 or 2).
    /// </summary>
    private static HttpMessageHandler ResourceFamilyDeliveringHandler(int rowCount)
    {
        var key1Values = Enumerable.Range(0, rowCount)
            .Select(static index => ((char)('a' + index)).ToString())
            .ToArray();
        return LuxembourgAcquisitionTestFixture.AllowRobotsThenHandler((ordinal, req) => ordinal switch
        {
            1 or 4 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.CountJson(rowCount)),
            2 or 5 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.RowsJson(key1Values)),
            3 or 6 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            _ => throw new AssertFailedException("No further sends after both passes complete."),
        });
    }

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

    private static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

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
    /// profile under test rather than fixed and hand-specified, and that no production
    /// <see cref="IScopeReductionEvidenceResolver"/> exists yet -- see the doc remark on that
    /// interface for who is expected to supply one. <c>FixedAdmittedSetEvidenceResolver</c> below is
    /// the fold-in's fixed-set counterpart.
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
    /// Fold-in two of the D1-04 refreeze: an evidence resolver whose admitted set is fixed and
    /// hand-specified -- transcribed from what a real run against <see cref="BuildProfile"/> and
    /// <see cref="AMatchingResourceCensusLetsScopeResolutionProceed"/>'s own observation actually
    /// presents, printed from a throwaway failing assertion rather than derived from the profile
    /// under test the way <see cref="PermissiveEvidenceResolver"/> is. Everything else is refused.
    /// Using this resolver in place of the permissive one for the same scenario and getting the same
    /// delivered result proves the adapter's wiring is reading exactly what a resolver admits, not
    /// merely something shaped like a SHA-256.
    /// </summary>
    private sealed class FixedAdmittedSetEvidenceResolver(SourceArtifactRef completeEnumerationRef)
        : IScopeReductionEvidenceResolver
    {
        // Transcribed, not computed: printed from a throwaway failing assertion
        // (ZZZ_PrintBindingsForFixedResolverTranscription, removed once these were copied out) that
        // ran exactly BuildProfile()'s snapshot against one ordinary LOI observation
        // ("http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1") over a one-row
        // resource-observation family. The object ref and selector-set digests are one value each
        // because every axis binds the same one observed object and one selector set; the evidence
        // and rule-evaluation digests are sets because R5.1 evaluates several axes
        // (record/body/relation/supportingDocument/family/language/format/authenticity/rights/
        // transport) over that one object, and each axis has its own selector evidence and its own
        // rule-evaluation outcome.
        private const string ObjectRefSha256 =
            "8fa6de8d8732399c7e3931fcc51ead455bd4c1f1001290ebe0526cb2075b7317";
        private const string SelectorSetSha256 =
            "c7d890e7479f494c3c8a7882995d4f76de5656abbc0b1dbb0b9b93b51930d7ab";

        private static readonly HashSet<string> AdmittedSelectorEvidenceDigests = new(StringComparer.Ordinal)
        {
            "711dd62e9f0418e13614daaf717b40c49be9211b386d7d0fbd318758b93dded8",
            "b8c0268e4b77cd359e470e3a73fd768e27c090911663acf1e5f7e4242533aa7a",
            "dc2c9fb5b1147229561547dfe4b858fe0819fb77c0cab6ea29f07ddd62c2ce19",
        };

        private static readonly HashSet<string> AdmittedRuleEvaluationDigests = new(StringComparer.Ordinal)
        {
            "03f2fc98d7897108bc447514034f4ed0396aa1e7ce0b01dbe279dcd497a6dfc2",
            "6aa58752c74a1b607ad945972dc74572c8424c10726f17c84fa3fc0ee6e30ff2",
            "83ad9b4b0b12bb784d7549538104113222633671ef13a927fa92727097313220",
            "83f921538089348128baec0ce7824d1df0af9b947039d4b60efb7aec6bdc210d",
        };

        public SourceArtifactRef CompleteEnumerationRef { get; } = completeEnumerationRef;

        public bool IsSelectorObservationAdmitted(ScopeSelectorObservationBinding binding) =>
            string.Equals(binding.ObjectRefSha256, ObjectRefSha256, StringComparison.Ordinal) &&
            AdmittedSelectorEvidenceDigests.Contains(binding.SelectorEvidenceSha256);

        public bool IsSelectorNotApplicableAdmitted(ScopeSelectorNotApplicableBinding binding) =>
            string.Equals(binding.ObjectRefSha256, ObjectRefSha256, StringComparison.Ordinal);

        public bool IsRuleEvaluationAdmitted(ScopeRuleEvaluationBinding binding) =>
            string.Equals(binding.ObjectRefSha256, ObjectRefSha256, StringComparison.Ordinal) &&
            string.Equals(binding.SelectorSetSha256, SelectorSetSha256, StringComparison.Ordinal) &&
            AdmittedRuleEvaluationDigests.Contains(binding.RuleEvaluationSha256);

        public bool IsCompleteEnumerationAdmitted(ScopeCompleteEnumerationBinding binding) =>
            binding.CompleteEnumerationRef == CompleteEnumerationRef;
    }
}
