using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;

namespace Lex.V3.Ingest.Europe;

public sealed partial class EuQueryExecutionAdapter
{
    private const string RunCheckpointSchema = "lex-eu-acquisition-checkpoint/1";

    public async Task<EuQueryExecutionResult> RunAsync(
        IReadOnlyList<(EuCensusPartitionRunRequest Request, BoundMachineRequest SourceWitness)> censusFamilies,
        EuObjectFactsBatchPolicy objectFactsPolicy, MachineQueryRendererSource witnessRendererSource,
        BoundMachineRequest witnessSourceWitness, MachineQueryRendererSource documentFetchRendererSource,
        BoundMachineRequest documentFetchSourceWitness, WireRequestBudget wireBudget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(censusFamilies);
        var seeds = censusFamilies.Select(static value => (value.Request, (BoundMachineRequest?)value.SourceWitness)).ToArray();
        var context = new RunCheckpointContext(null);
        var result = await RunCoreAsync(seeds, objectFactsPolicy, witnessRendererSource, witnessSourceWitness,
            documentFetchRendererSource, documentFetchSourceWitness, wireBudget, cancellationToken, context).ConfigureAwait(false);
        if (result.Refusal is not null) return result;
        try
        {
            // A renderer can be referenced even when policy selected no requests for its role.
            // Retain every named source explicitly so the catalog has a complete offline closure.
            var renderers = new List<MachineQueryRendererSource>
                { objectFactsPolicy.RendererSource, witnessRendererSource, documentFetchRendererSource };
            foreach (var seed in seeds) renderers.Add(seed.Request.RendererSource);
            var retainedSources = new HashSet<string>(StringComparer.Ordinal);
            foreach (var renderer in renderers)
            {
                if (!retainedSources.Add(renderer.Reference.Sha256)) continue;
                var sourceBytes = renderer.CopyBytes();
                var (sourceReceipt, sourceFailure) = await CustodyHold.TryHoldAsync(_custodyStore, sourceBytes, cancellationToken).ConfigureAwait(false);
                if (sourceReceipt is null) throw new CustodyRequiredException("Acquisition renderer hold refused: " + sourceFailure);
                if (sourceReceipt.Reference.ContentSha256 != renderer.Reference.Sha256 || sourceReceipt.Reference.ByteLength != sourceBytes.Length)
                    throw new CustodyIntegrityException("Acquisition renderer receipt names different bytes.");
            }
            var document = new RunCheckpoint(RunCheckpointSchema, context.Census.ToArray(), objectFactsPolicy.PlanResourceId,
                objectFactsPolicy.RendererSource.Reference, witnessRendererSource.Reference, documentFetchRendererSource.Reference,
                context.Objects.ToArray(), context.Tripwires.ToArray(), context.Witness!.CheckpointRef!,
                context.Witness.AcquisitionRunRef!, result.DocumentAcquisitionCheckpointRef!, context.Manifest!, context.Run!,
                result.CorpusRecordSetRef!, RunResultDigest(result));
            if (document.Census.Any(static family => family.Checkpoint is null) ||
                document.Objects.Any(static family => family.Checkpoint is null) || document.Witness is null ||
                document.WitnessRun is null || document.Documents is null)
                throw new CustodyIntegrityException("A successful acquisition omitted a dependency checkpoint.");
            var bytes = EncodeRunCheckpoint(document);
            var (receipt, failure) = await CustodyHold.TryHoldAsync(_custodyStore, bytes, cancellationToken).ConfigureAwait(false);
            if (receipt is null) throw new CustodyRequiredException("Acquisition checkpoint hold refused: " + failure);
            if (receipt.Reference.ContentSha256 != CustodyDigest.Of(bytes) || receipt.Reference.ByteLength != bytes.Length)
                throw new CustodyIntegrityException("Acquisition checkpoint receipt names different bytes.");
            return result.WithAcquisitionCheckpoint(new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", receipt.Reference.ContentSha256));
        }
        catch (Exception exception) when (exception is CustodyRequiredException or CustodyIntegrityException)
        {
            return EuQueryExecutionResult.Refused(result.Topology, result.FamilyOutcomes,
                new EuQueryExecutionRefusalDetail(EuQueryExecutionRefusal.AcquisitionCheckpointNotRetained, exception.Message));
        }
    }

    /// <summary>
    /// Restores the complete EU query run from retained acquisition evidence. The ordered seeds
    /// are supplied by the containing build catalog. No publisher session is constructed.
    /// </summary>
    public static async Task<EuQueryExecutionResult> ReopenAsync(ICustodyStore store, SourceArtifactRef checkpoint,
        IReadOnlyList<string> expectedSeeds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(expectedSeeds);
        var seeds = expectedSeeds.ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, cancellationToken).ConfigureAwait(false);
        try
        {
            var document = ContractJson.Deserialize<RunCheckpoint>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (document is null || document.Schema != RunCheckpointSchema || !bytes.Span.SequenceEqual(EncodeRunCheckpoint(document)) ||
                document.Census is null || document.Objects is null || document.Tripwires is null || document.Census.Length == 0 ||
                document.Witness is null || document.WitnessRun is null || document.Documents is null ||
                document.Manifest is null || document.Run is null || document.Corpus is null ||
                document.ObjectRenderer is null || document.WitnessRenderer is null || document.DocumentRenderer is null ||
                document.Census.Concat(document.Objects).Any(static family => family is null || family.Checkpoint is null ||
                    family.Run is null || family.Profile is null || family.Renderer is null) ||
                document.Census.Any(static value => value is null || value.Celex is null || value.Set is not null || value.Batch is not null) ||
                document.Objects.Any(static value => value is null || value.Celex is not null || value.Set is null || value.Batch is null) ||
                !seeds.SequenceEqual(document.Census.Select(static value => value.Celex), StringComparer.Ordinal) ||
                seeds.Distinct(StringComparer.Ordinal).Count() != seeds.Length ||
                document.Census.Concat(document.Objects).Select(static value => value.Run).Distinct().Count() != document.Census.Length + document.Objects.Length ||
                document.Tripwires.Any(static value => value is null || value.Checkpoint is null) ||
                document.Tripwires.Select(static value => value.FamilyKey).Distinct(StringComparer.Ordinal).Count() != document.Tripwires.Length)
                throw new CustodyIntegrityException("Acquisition checkpoint framing or requested seed scope disagrees.");
            var budget = WireRequestBudget.OfWireRequests(2);
            var requests = new List<(EuCensusPartitionRunRequest Request, BoundMachineRequest? SourceWitness)>();
            foreach (var family in document.Census)
            {
                var renderer = await OpenRunRendererAsync(store, family.Renderer, cancellationToken).ConfigureAwait(false);
                requests.Add((new EuCensusPartitionRunRequest(EuConsolidationDiscoveryPlan.Create(), family.PlanResourceId,
                    family.Celex!, renderer, budget), null));
            }
            var objects = await OpenRunRendererAsync(store, document.ObjectRenderer, cancellationToken).ConfigureAwait(false);
            var witness = await OpenRunRendererAsync(store, document.WitnessRenderer, cancellationToken).ConfigureAwait(false);
            var documents = await OpenRunRendererAsync(store, document.DocumentRenderer, cancellationToken).ConfigureAwait(false);
            // This private replay uses only the policy's plan/renderer fields. Every execution
            // branch is replaced by checked custody replay and refuses missing associations.
            var policy = new EuObjectFactsBatchPolicy(EuObjectFactsDiscoveryPlan.Create(), document.ObjectPlanResourceId, objects, null!);
            var context = new RunCheckpointContext(document);
            var result = await new EuQueryExecutionAdapter(store, new EuRepeatedEnumerationExecutor(store, TimeProvider.System))
                .RunCoreAsync(requests, policy, witness, null, documents, null, budget, cancellationToken, context).ConfigureAwait(false);
            context.RequireEnd();
            if (result.Refusal is not null || budget.Spent != 0 || RunResultDigest(result) != document.ResultSha256)
                throw new CustodyIntegrityException("Retained acquisition did not reproduce its original result: " + result.Refusal?.Detail);
            return result.WithAcquisitionCheckpoint(checkpoint);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("Acquisition checkpoint failed independent verification.", exception);
        }
    }

    // The containing EU catalog must name the same sources that actually bound this query run.
    internal static async Task VerifyRendererBindingsAsync(ICustodyStore store, SourceArtifactRef checkpoint,
        EuRendererSources expected, CancellationToken cancellationToken)
    {
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, cancellationToken).ConfigureAwait(false);
        try
        {
            var document = ContractJson.Deserialize<RunCheckpoint>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (document is null || document.Schema != RunCheckpointSchema || !bytes.Span.SequenceEqual(EncodeRunCheckpoint(document)) ||
                document.Census is null || document.Objects is null || document.Census.Length == 0 ||
                document.Census.Any(family => family is null || family.Renderer != expected.Census.Reference) ||
                document.Objects.Any(family => family is null || family.Renderer != expected.ObjectFacts.Reference) ||
                document.ObjectRenderer != expected.ObjectFacts.Reference || document.WitnessRenderer != expected.Witness.Reference ||
                document.DocumentRenderer != expected.DocumentFetch.Reference)
                throw new CustodyIntegrityException("Containing EU catalog disagrees with the query run's renderer roles.");
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("Query renderer bindings failed independent verification.", exception);
        }
    }

    private static async Task<MachineQueryRendererSource> OpenRunRendererAsync(ICustodyStore store,
        SourceArtifactRef reference, CancellationToken cancellationToken)
    {
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, reference.Sha256, cancellationToken).ConfigureAwait(false);
        return MachineQueryRendererSource.Open(reference, bytes.Span);
    }

    private static byte[] EncodeRunCheckpoint(RunCheckpoint value) => Encoding.UTF8.GetBytes(ContractJson.Serialize(value));
    private static string RunResultDigest(EuQueryExecutionResult result) => CustodyDigest.Of(Encoding.UTF8.GetBytes(ContractJson.Serialize(new
    {
        result.ScopeManifestCanonicalSha256, result.CorpusRecordSetRef, result.ObservedObjectCount, result.ObservedExpressionCount,
        result.FamilyOutcomes, result.ReductionExclusions, result.ObservedManifestationTypesByCelex, result.ObservedExpressionsByCelex,
        result.MintedRowsByOrdinal, result.DateAxioms, result.LocatedAmendmentObservations, result.WitnessTerminations,
    })));

    private sealed record RunCheckpoint(string Schema, RunFamily[] Census, string ObjectPlanResourceId,
        SourceArtifactRef ObjectRenderer, SourceArtifactRef WitnessRenderer, SourceArtifactRef DocumentRenderer,
        RunFamily[] Objects, RunTripwire[] Tripwires, SourceArtifactRef Witness, SourceArtifactRef WitnessRun,
        SourceArtifactRef Documents, SourceArtifactRef Manifest, SourceArtifactRef Run, SourceArtifactRef Corpus, string ResultSha256);
    private sealed record RunFamily(SourceArtifactRef Checkpoint, SourceArtifactRef Run, SourceArtifactRef Profile,
        string PlanResourceId, SourceArtifactRef Renderer, string? Celex, EuObjectFactsQuerySet? Set, string[]? Batch);
    private sealed record RunTripwire(string FamilyKey, SourceArtifactRef Checkpoint);

    private sealed class RunCheckpointContext(RunCheckpoint? original)
    {
        internal RunCheckpoint? Original { get; } = original;
        internal bool IsReplay => Original is not null;
        internal List<RunFamily> Census { get; } = [];
        internal List<RunFamily> Objects { get; } = [];
        internal List<RunTripwire> Tripwires { get; } = [];
        internal SourceArtifactRef? Manifest { get; set; }
        internal SourceArtifactRef? Run { get; set; }
        internal EuWitnessTraversalResult? Witness { get; set; }
        private int _censusOrdinal, _objectOrdinal;
        private readonly HashSet<string> _consumedTripwires = new(StringComparer.Ordinal);
        private readonly Dictionary<(EuObjectFactsQuerySet, string), RepeatedEnumerationDeliveryReceipt> _openedObjects = [];

        internal void RecordCensus(EuCensusPartitionRunRequest request, EuEnumerationRunResult result)
        {
            if (IsReplay || result.Receipt is null) return;
            Census.Add(Describe(result, request.PlanResourceId, request.RendererSource.Reference, request.RequestedCelex, null, null));
        }
        internal void RecordObjects(EuObjectFactsPartitionRunRequest request, EuEnumerationRunResult result,
            EuCorrigendumTripwireProductionResult? production)
        {
            if (IsReplay || result.Receipt is null) return;
            Objects.Add(Describe(result, request.PlanResourceId, request.RendererSource.Reference, null, request.Set,
                EuObjectFactsDiscoveryPlan.RequestedPartitionMembers(request.BatchObjects).ToArray()));
            if (production?.Delivered == true)
                Tripwires.Add(new(result.Receipt.Delivery.PartitionKey,
                    production.CheckpointRef ?? throw new CustodyIntegrityException("Tripwire checkpoint absent.")));
        }
        private static RunFamily Describe(EuEnumerationRunResult result, string plan, SourceArtifactRef renderer,
            string? celex, EuObjectFactsQuerySet? set, string[]? batch) => new(result.CheckpointRef!,
                result.Receipt!.Delivery.RunIdentity, result.Receipt.Delivery.InterpretationProfileRef, plan, renderer, celex, set, batch);

        internal async Task<EuEnumerationRunResult> OpenCensusAsync(ICustodyStore store, EuCensusPartitionRunRequest request,
            CancellationToken cancellationToken)
        {
            if (_censusOrdinal >= Original!.Census.Length) throw new CustodyIntegrityException("Census checkpoint omitted a seed.");
            var family = Original.Census[_censusOrdinal++];
            if (family.Celex != request.RequestedCelex || family.PlanResourceId != request.PlanResourceId || family.Renderer != request.RendererSource.Reference)
                throw new CustodyIntegrityException("Census checkpoint request disagrees.");
            return await OpenFamilyAsync(store, family, request.RendererSource, cancellationToken).ConfigureAwait(false);
        }
        internal async Task<EuEnumerationRunResult> OpenObjectsAsync(ICustodyStore store, EuObjectFactsPartitionRunRequest request,
            CancellationToken cancellationToken)
        {
            if (_objectOrdinal >= Original!.Objects.Length) throw new CustodyIntegrityException("Object checkpoint omitted a derived batch.");
            var family = Original.Objects[_objectOrdinal++];
            if (family.Set != request.Set || family.PlanResourceId != request.PlanResourceId || family.Renderer != request.RendererSource.Reference ||
                !family.Batch!.SequenceEqual(EuObjectFactsDiscoveryPlan.RequestedPartitionMembers(request.BatchObjects), StringComparer.Ordinal))
                throw new CustodyIntegrityException("Object checkpoint disagrees with the batch derived from proven census closure.");
            var result = await OpenFamilyAsync(store, family, request.RendererSource, cancellationToken).ConfigureAwait(false);
            if (!_openedObjects.TryAdd((request.Set, result.Receipt!.Delivery.PartitionKey), result.Receipt))
                throw new CustodyIntegrityException("Object checkpoint repeats a family.");
            return result;
        }
        private static async Task<EuEnumerationRunResult> OpenFamilyAsync(ICustodyStore store, RunFamily family,
            MachineQueryRendererSource renderer, CancellationToken cancellationToken)
        {
            var receipt = await EuEnumerationCheckpoint.RestoreReceiptAsync(store, family.Checkpoint, family.Run, family.Profile,
                cancellationToken).ConfigureAwait(false);
            var profile = family.Set is { } set ? EuObjectFactsDiscoveryPlan.Create().CreateDeliveryProfile(set)
                : EuConsolidationDiscoveryPlan.Create().CreateDeliveryProfile(EuConsolidationQuerySet.Family);
            RepeatedEnumerationInterpretationProfileIdentity.Validate(family.Profile, profile);
            var glue = new RepeatedEnumerationDeliveryReopenGlue(store);
            foreach (var count in new[] { receipt.Delivery.CountA, receipt.Delivery.CountB })
            {
                var evidence = await glue.ReopenPageEvidenceAsync(count, cancellationToken).ConfigureAwait(false);
                var parameter = evidence.QueryInput.OrderedParameters.Single(value => value.Name == "pass_id");
                if (parameter.Kind != MachineQueryParameterKind.BoundedInteger || parameter.IntegerValue is not { } pass ||
                    count.QueryPlanRef.ResourceId != family.PlanResourceId)
                    throw new CustodyIntegrityException("Retained count has no matching plan/pass identity.");
                BoundMachineRequest request;
                SourceArtifactRef input;
                MachineQueryPlan plan;
                if (family.Set is { } familySet)
                {
                    var bound = EuObjectFactsDiscoveryPlan.Create().BindCount(familySet, family.Batch!,
                        (EuObjectFactsQueryPass)checked((int)pass), family.PlanResourceId, count.QueryInputRef.ResourceId, renderer);
                    request = bound.Request; input = bound.InputArtifact.ArtifactRef; plan = bound.MachinePlan;
                }
                else
                {
                    var bound = EuConsolidationDiscoveryPlan.Create().BindCount(EuConsolidationQuerySet.Family, family.Celex!,
                        (EuConsolidationQueryPass)checked((int)pass), family.PlanResourceId, count.QueryInputRef.ResourceId, renderer);
                    request = bound.Request; input = bound.InputArtifact.ArtifactRef; plan = bound.MachinePlan;
                }
                MachineQueryPlanIdentity.Validate(count.QueryPlanRef, plan);
                var held = await CustodyRestore.ReadByDigestCheckedAsync(store, evidence.LogicalRequest.Body.Sha256,
                    cancellationToken).ConfigureAwait(false);
                if (input != count.QueryInputRef || !held.Span.SequenceEqual(MachineQueryBinder.OpenForSend(request).CopyRequestBody()))
                    throw new CustodyIntegrityException("Retained count differs from its closed request template.");
            }
            return EuEnumerationRunResult.DeliveredWithCheckpoint(receipt, 0, family.Checkpoint);
        }
        internal async Task<EuCorrigendumTripwireProductionResult> OpenTripwireAsync(ICustodyStore store,
            EuObjectFactsPartitionRunRequest request, CancellationToken cancellationToken)
        {
            var key = EuObjectFactsDiscoveryPlan.PartitionKeyFor(request.BatchObjects);
            var matches = Original!.Tripwires.Where(value => value.FamilyKey == key).ToArray();
            if (matches.Length != 1 || !_consumedTripwires.Add(key)) throw new CustodyIntegrityException("Tripwire pairing missing or repeated.");
            var production = await EuCorrigendumTripwireProducer.ReopenAsync(store, matches[0].Checkpoint, cancellationToken).ConfigureAwait(false);
            if (!production.Delivered) throw new CustodyIntegrityException("Tripwire pairing refused: " + production.Detail);
            var derivation = production.Expressions!.Derivation!;
            RequireSameProof(derivation.ExpressionFactsProof, EuObjectFactsQuerySet.ExpressionFacts, key);
            RequireSameProof(derivation.ObjectFactsProof!, EuObjectFactsQuerySet.ObjectFacts, key);
            return production;
        }
        private void RequireSameProof(AbsenceFamilyEnumerationProof actual, EuObjectFactsQuerySet set, string key)
        {
            if (!_openedObjects.TryGetValue((set, key), out var receipt)) throw new CustodyIntegrityException("Paired family absent from this run.");
            var expected = receipt.TryProveFamilyEnumeration(key, out var refusal)
                ?? throw new CustodyIntegrityException("Paired family did not prove: " + refusal);
            if (ContractJson.Serialize(actual) != ContractJson.Serialize(expected))
                throw new CustodyIntegrityException("Tripwire pairing refers to another family proof.");
        }
        internal void RequireEnd()
        {
            if (_censusOrdinal != Original!.Census.Length || _objectOrdinal != Original.Objects.Length ||
                _consumedTripwires.Count != Original.Tripwires.Length)
                throw new CustodyIntegrityException("Acquisition checkpoint has unconsumed dependencies.");
        }
    }
}
