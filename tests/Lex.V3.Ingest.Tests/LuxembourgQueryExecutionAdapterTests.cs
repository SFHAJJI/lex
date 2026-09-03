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
/// </summary>
[TestClass]
public sealed class LuxembourgQueryExecutionAdapterTests
{
    private const string RelationSetId = "G";
    private const string RelationFamilyKey = "relation-assertions";
    private const string JoluxAct = "http://data.legilux.public.lu/resource/ontology/jolux#Act";
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
            [], null, [], new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

        Assert.IsNotNull(result.Topology);
        Assert.AreEqual(
            LuxembourgSourceProfileTopology.SinglePublisherStoreMemberKey,
            result.Topology.Topology.MemberKey);
        Assert.AreEqual(profile.ScopeBinding.SourceProfileRef, result.Topology.IdentityProfileRef);
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
            [], null, [], new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

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
            [], RelationFamilyKey, [], new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

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
            [(partitionRequest, witness)], RelationFamilyKey, [],
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
            [(partitionRequest, witness)], RelationFamilyKey, [],
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
    }

    [TestMethod]
    public async Task AMismatchedObservationEnumerationRefusesScopeResolution()
    {
        var (profile, observationRef, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var adapter = new LuxembourgQueryExecutionAdapter(
            store, NewExecutor(store, NoSendHandler()), profile);
        var wrongRef = new SourceArtifactRef(
            "urn:uuid:00000000-0000-4000-8000-0000000000ee", new string('9', 64));
        var badObservation = Observation(
            observationRef: wrongRef,
            publisherUri: "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1",
            typeDocumentIri: TypeDocumentPrefix + "LOI");
        _ = observationRef;

        var result = await adapter.RunAsync(
            [], null, [badObservation], new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

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
                [], null, [], new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

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
            "ACC", LuxembourgCoarseDispositionGap.AccConstitutionalReviewEvidenceGateNotApplied);

    [TestMethod]
    public async Task AnOrdinaryLoiActCarriesNoCoarseDispositionMarker()
    {
        var (profile, observationRef, enumerationRef) = BuildProfile();
        var store = new InMemoryCustodyStore();
        var adapter = new LuxembourgQueryExecutionAdapter(
            store, NewExecutor(store, NoSendHandler()), profile);
        var observation = Observation(
            observationRef,
            "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1",
            TypeDocumentPrefix + "LOI");

        var result = await adapter.RunAsync(
            [], null, [observation], new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

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
            var adapter = new LuxembourgQueryExecutionAdapter(
                store, NewExecutor(store, handler), profile);
            var (partitionRequest, witness) = BuildPartitionRequest(RelationSetId, RelationFamilyKey);

            var result = await adapter.RunAsync(
                [(partitionRequest, witness)], RelationFamilyKey, [],
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
        var adapter = new LuxembourgQueryExecutionAdapter(
            store, NewExecutor(store, NoSendHandler()), profile);
        const string publisherUri = "http://data.legilux.public.lu/eli/etat/leg/loi/2026/01/01/a1";
        var observation = Observation(observationRef, publisherUri, TypeDocumentPrefix + typeDocumentSuffix);

        var result = await adapter.RunAsync(
            [], null, [observation], new PermissiveEvidenceResolver(enumerationRef), CancellationToken.None);

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
        SourceArtifactRef observationRef, string publisherUri, string typeDocumentIri)
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
                publisherUri, RdfType, LuxembourgAssertionObjectKind.Iri, JoluxAct, "", "", observationRef),
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
