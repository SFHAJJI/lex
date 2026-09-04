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

    [TestMethod]
    public async Task TopologyIsAlwaysMintedEvenOnRefusal()
    {
        var (profile, _, enumerationRef) = BuildProfile();
        var adapter = new LuxembourgQueryExecutionAdapter(
            new InMemoryCustodyStore(), NewExecutor(new InMemoryCustodyStore(), NoSendHandler()),
            profile);

        var result = await adapter.RunAsync(
            [], null, null, new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

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
            [], null, null, new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

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
            [], RelationFamilyKey, null, new PermissiveEvidenceResolver(enumerationRef),
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
            [(partitionRequest, witness)], RelationFamilyKey, null,
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
            [(partitionRequest, witness)], RelationFamilyKey, null,
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
            [(partitionRequest, witness)], RelationFamilyKey, null,
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
            [(partitionRequest, witness)], null, null,
            new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

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
            [], null, ResourceFamilyKey, new PermissiveEvidenceResolver(enumerationRef),
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
            [(partitionRequest, witness)], null, ResourceFamilyKey,
            new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

        Assert.AreEqual(LuxembourgFamilyEnumerationOutcomeKind.ProofRefused, result.FamilyOutcomes.Single().Kind);
        Assert.IsNull(result.ScopeManifestReceipt);
        Assert.IsNotNull(result.Refusal);
        Assert.AreEqual(LuxembourgQueryExecutionRefusal.ResourceObservationFamilyNotProven, result.Refusal.Code);
    }

    [TestMethod]
    public async Task TheResourceDerivationMatchesTheDesignatedFamilyByKeyNotByPosition()
    {
        // Two families enumerated in one run, both Proven, the designated resource family listed
        // SECOND. A derivation that picked "the first Proven outcome" or "any Proven outcome" rather
        // than the outcome whose FamilyKey equals resourceObservationFamilyKey would try to decode
        // the relation family's own "subject, predicate, object, key_1..key_6" rows as if they were
        // the resource family's plain key_1..key_6 rows and disagree with that family's own proof,
        // refusing instead of succeeding.
        //
        // Each family is its own RoutedHttpAcquisitionSession (LuxembourgRepeatedEnumerationExecutor
        // .RunPartitionAsync starts a fresh one every call), and SequencedHandler's ordinal counter
        // is one running total across the whole shared handler instance, so this handler answers
        // robots twice -- once at ordinal 0 for the relation family's own bootstrap, once at ordinal
        // 7 for the resource family's.
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var (relationRequest, relationWitness) = BuildPartitionRequest(RelationSetId, RelationFamilyKey);
        var (resourceRequest, resourceWitness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);
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
                req, LuxembourgAcquisitionTestFixture.RowsJson(
                    "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a0")),
            10 or 13 => LuxembourgAcquisitionTestFixture.JsonResponse(
                req, LuxembourgAcquisitionTestFixture.EmptyRowsJson()),
            _ => throw new AssertFailedException($"unexpected ordinal {ordinal}"),
        });
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);

        var result = await adapter.RunAsync(
            [(relationRequest, relationWitness), (resourceRequest, resourceWitness)],
            RelationFamilyKey, ResourceFamilyKey,
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
    public async Task AProvenResourceFamilysRowsDeriveObservationsAndLetScopeResolutionProceed()
    {
        // The positive control: one genuinely delivered, independently re-verified row, decoded
        // through item 17 into one LuxembourgResourceObservation with no caller-supplied list
        // anywhere in the call. Two rows exercises the per-row mapping loop, not just a single
        // iteration.
        var (profile, _, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var handler = ResourceFamilyDeliveringHandler(rowCount: 2);
        var adapter = new LuxembourgQueryExecutionAdapter(store, NewExecutor(store, handler), profile);
        var (partitionRequest, witness) = BuildPartitionRequest(ResourceSetId, ResourceFamilyKey);

        var result = await adapter.RunAsync(
            [(partitionRequest, witness)], null, ResourceFamilyKey,
            new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

        Assert.IsNull(result.Refusal, $"code={result.Refusal?.Code} detail={result.Refusal?.Detail}");
        Assert.IsNotNull(result.ScopeManifestReceipt);
        Assert.AreEqual(LuxembourgQueryExecutionCompletion.AllFamiliesProven, result.Completion);
        // The "subjects" family projects only a bare resource identity (no rdf:type, no
        // typeDocument), so a genuinely derived observation can never resolve to AcceptedCandidate
        // and this run's coarse-disposition markers must be empty -- proving the derivation actually
        // ran (an unreached derivation would also show zero markers, but would have refused above).
        Assert.AreEqual(0, result.CoarseDispositionMarkers.Count);
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
                [], null, null, new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

            Assert.IsNull(result.ScopeManifestReceipt);
            Assert.IsNotNull(result.Refusal);
            Assert.AreEqual(LuxembourgQueryExecutionRefusal.ScopeManifestNotHeld, result.Refusal.Code);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // D1-04a's coarse-disposition-marker tests (ATcResourceAcceptedByBucketMembershipCarriesThe...,
    // AnOrdinaryLoiActCarriesNoCoarseDispositionMarker, and their AssertCoarseGapAsync helper) drove
    // BuildCoarseDispositionMarkers through hand-crafted rdf:type/typeDocument assertions on a
    // caller-supplied observations list. D1-04b removes that list: this adapter now derives
    // observations only from the "subjects" family's own rows, which carry no assertions at all (see
    // MapRowsToResourceObservations' remarks on LuxembourgQueryExecutionAdapter), so a genuinely
    // derived resource can never resolve to AcceptedCandidate and those scenarios are no longer
    // reachable through this adapter -- not a corner cut, a direct, named consequence of removing the
    // caller-supplied list. AProvenResourceFamilysRowsDeriveObservationsAndLetScopeResolutionProceed
    // above proves the wiring still calls BuildCoarseDispositionMarkers correctly against real derived
    // data (finding nothing to mark, honestly, because there is nothing to find); it does NOT exercise
    // BuildCoarseDispositionMarkers' own gap-found branch (locating the typeDocument assertion and
    // mapping a PriorityCandidateType to its LuxembourgCoarseDispositionGap), which needs an
    // AcceptedCandidate resource this family alone cannot produce. That branch's own unit-level
    // correctness is unrelated to family sourcing and was never adapter-specific logic under test
    // here in the first place; restoring adapter-level coverage of it is future work for whichever
    // slice wires a resource-observation source that actually carries assertions (see this file's own
    // report on the "A" family finding).

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
                [(partitionRequest, witness)], RelationFamilyKey, null,
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
    /// Robots allow, then a <paramref name="rowCount"/>-row page and an empty terminal page, on
    /// both passes, using the plain "subjects" template's own key_1..key_6 projection (no
    /// relation-assertions style extra columns): the same generic shape D1-03's own executor tests
    /// use for a resource family. D1-04b's own derivation decodes <c>key_1</c> as the resource's
    /// publisher URI (<see cref="LuxembourgQueryExecutionAdapter.MapRowsToResourceObservations"/>),
    /// which <see cref="Lex.V3.Contracts.Source.Core.SourceObjectRef"/> requires to be a genuine
    /// absolute HTTP(S) URI -- unlike D1-04a's own bare single-letter row keys, which only ever fed a
    /// row count. Row keys are therefore distinct, strictly ascending absolute URIs, which this
    /// file's own <see cref="BuildPartitionRequest"/> range ("" to "￿") still contains. Requires
    /// <paramref name="rowCount"/> to be at least 1 and small enough to fit in one page (every
    /// scenario in this file uses 1 or 2).
    /// </summary>
    private static HttpMessageHandler ResourceFamilyDeliveringHandler(int rowCount)
    {
        var key1Values = Enumerable.Range(0, rowCount)
            .Select(static index =>
                $"http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a{index}")
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
    /// interface for who is expected to supply one. D1-04a's own fixed-set counterpart,
    /// <c>FixedAdmittedSetEvidenceResolver</c>, transcribed digests off the caller-supplied
    /// observation shape that parameter carried; D1-04b removed that parameter and, with it, the
    /// exact bytes those transcribed digests were taken from, so that counterpart is removed rather
    /// than re-transcribed against a shape it never actually reads a hand-supplied value for any
    /// more.
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
}
