using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Absence;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Ingest.Luxembourg;

public sealed partial class LuxembourgQueryExecutionAdapter
{
    private const string QueryCheckpointSchema = "lex-lu-query-acquisition-checkpoint/1";

    /// <summary>Rederives a retained, fully proven S/A/G acquisition with no publisher requests.</summary>
    /// <remarks>The caller supplies an independently reopened vocabulary profile and the intended ranges.</remarks>
    internal static async Task<LuxembourgQueryExecutionResult> ReopenAcquisitionAsync(ICustodyStore store,
        SourceArtifactRef checkpoint, VerifiedLuxembourgSourceProfile profile,
        IReadOnlyList<LuxembourgQueryPartitionRange> expectedRanges, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(expectedRanges);
        cancellationToken.ThrowIfCancellationRequested();
        var ranges = expectedRanges.ToArray();
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, cancellationToken).ConfigureAwait(false);
        try
        {
            var document = ContractJson.Deserialize<QueryCheckpoint>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (document is null || document.Schema != QueryCheckpointSchema || !bytes.Span.SequenceEqual(EncodeQuery(document)) ||
                document.ProfileSha256 != HashDocument(profile.Snapshot) || document.Families is null ||
                document.Families.Any(static family => family is null || family.Range is null || family.Plan is null ||
                    family.Renderer is null || family.Checkpoint is null || family.Run is null || family.Profile is null) ||
                document.DocumentRenderer is null ||
                !document.Families.Select(static family => family.Range).SequenceEqual(ranges) ||
                ranges.Select(static range => range.PartitionId).Distinct(StringComparer.Ordinal).Count() != ranges.Length ||
                document.Documents is null || document.Gazette is null || document.Corpus is null || document.Observed is null ||
                document.Run is null || document.SelectionManifest is null || document.FinalManifest is null)
                throw new CustodyIntegrityException("LU acquisition catalog framing, profile or intended scope disagrees.");
            var rendererBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, document.DocumentRenderer.Sha256, cancellationToken).ConfigureAwait(false);
            var renderer = MachineQueryRendererSource.Open(document.DocumentRenderer, rendererBytes.Span);
            var replay = new QueryReplay(document);
            var families = new List<(LuxembourgPartitionRunRequest, BoundMachineRequest, LuxembourgPartitionChain?)>();
            foreach (var family in document.Families)
            {
                var planBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, family.PlanWireSha256, cancellationToken).ConfigureAwait(false);
                var plan = LuxembourgQueryPlan.ParseAndVerify(family.Plan, planBytes.Span);
                var sourceBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, family.Renderer.Sha256, cancellationToken).ConfigureAwait(false);
                var source = MachineQueryRendererSource.Open(family.Renderer, sourceBytes.Span);
                var request = new LuxembourgPartitionRunRequest(plan, family.Plan.ResourceId, family.Set, family.Range, source);
                var legs = new List<FamilyRowsLeg>();
                if (family.Kind == LuxembourgFamilyEnumerationOutcomeKind.Proven)
                {
                    var receipt = await LuxembourgEnumerationCheckpoint.RestoreReceiptAsync(store, family.Checkpoint,
                        family.Run, family.Profile, cancellationToken).ConfigureAwait(false);
                    var proof = receipt.TryProveFamilyEnumeration(family.Range.PartitionId, out var refusal)
                        ?? throw new CustodyIntegrityException("Retained LU family no longer proves: " + refusal);
                    legs.Add(new(proof, receipt, request));
                }
                else if (family.Kind == LuxembourgFamilyEnumerationOutcomeKind.CoverProven)
                {
                    var cover = await LuxembourgPartitionCoverCheckpoint.RestoreAsync(store, family.Checkpoint,
                        family.Range, family.Run, family.Profile, cancellationToken).ConfigureAwait(false);
                    for (var index = 0; index < cover.Chain.Leaves.Count; index++)
                    {
                        var leaf = cover.Chain.Leaves[index];
                        var receipt = cover.LeafReceipts[index];
                        var proof = receipt.TryProveFamilyEnumeration(leaf.PartitionId, out var refusal)
                            ?? throw new CustodyIntegrityException("Retained LU leaf no longer proves: " + refusal);
                        legs.Add(new(proof, receipt, request with { Partition = leaf }));
                    }
                }
                else throw new CustodyIntegrityException("The LU catalog requires proven original families.");
                if (legs.Count == 0) throw new CustodyIntegrityException("A retained LU family has no proven legs.");
                BoundMachineRequest? first = null;
                foreach (var leg in legs)
                {
                    var original = await VerifyQueryTemplateAsync(store, leg, cancellationToken).ConfigureAwait(false);
                    first ??= original;
                }
                replay.Legs.Add(family.Range.PartitionId, legs);
                // A bound retained count supplies the required input type. Replay never executes this witness.
                families.Add((request, first!, null));
            }
            var budget = WireRequestBudget.OfWireRequests(2);
            var adapter = new LuxembourgQueryExecutionAdapter(store,
                new LuxembourgRepeatedEnumerationExecutor(store, TimeProvider.System), profile);
            var result = await adapter.RunCoreAsync(families,
                Keys(document.Families, "G"), Keys(document.Families, "S"), Keys(document.Families, "A"),
                null, renderer, document.Scoped, budget, cancellationToken, null, document.Adaptive, replay).ConfigureAwait(false);
            if (result.Refusal is not null || budget.Spent != 0 || QueryResultDigest(result) != document.ResultSha256)
                throw new CustodyIntegrityException("LU acquisition replay differs from its original derivation: " + result.Refusal?.Detail);
            return result.WithAcquisitionCheckpoint(checkpoint);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("LU acquisition catalog failed independent verification.", exception);
        }
    }

    private static async Task<BoundMachineRequest> VerifyQueryTemplateAsync(ICustodyStore store,
        FamilyRowsLeg leg, CancellationToken cancellationToken)
    {
        var request = leg.PartitionRequest;
        var profile = request.InvariantPlan.CreateDeliveryProfile(request.InvariantPlanResourceId, request.SetId);
        RepeatedEnumerationInterpretationProfileIdentity.Validate(leg.Receipt.Delivery.InterpretationProfileRef, profile);
        var glue = new RepeatedEnumerationDeliveryReopenGlue(store);
        BoundMachineRequest? first = null;
        foreach (var count in new[] { leg.Receipt.Delivery.CountA, leg.Receipt.Delivery.CountB })
        {
            var evidence = await glue.ReopenPageEvidenceAsync(count, cancellationToken).ConfigureAwait(false);
            var passes = evidence.QueryInput.OrderedParameters.Where(static value => value.Name == "pass_id").ToArray();
            if (passes.Length != 1 || passes[0].Kind != MachineQueryParameterKind.BoundedInteger ||
                passes[0].IntegerValue is not { } pass || pass is not (1 or 2))
                throw new CustodyIntegrityException("Retained LU count has no single bounded pass identity.");
            var bound = request.InvariantPlan.BindCount(request.InvariantPlanResourceId, count.QueryPlanRef.ResourceId,
                count.QueryInputRef.ResourceId, request.SetId, (LuxembourgQueryPass)(int)pass, request.Partition, request.RendererSource);
            MachineQueryPlanIdentity.Validate(count.QueryPlanRef, bound.MachinePlan);
            var held = await CustodyRestore.ReadByDigestCheckedAsync(store, evidence.LogicalRequest.Body.Sha256, cancellationToken).ConfigureAwait(false);
            if (bound.InputArtifact.ArtifactRef != count.QueryInputRef ||
                !held.Span.SequenceEqual(MachineQueryBinder.OpenForSend(bound.Request).CopyRequestBody()))
                throw new CustodyIntegrityException("Retained LU query differs from the closed plan, role or range.");
            first ??= bound.Request;
        }
        return first!;
    }

    private async Task<SourceArtifactRef?> RetainQueryCheckpointAsync(
        IReadOnlyList<(LuxembourgPartitionRunRequest PartitionRequest, BoundMachineRequest SourceWitness, LuxembourgPartitionChain? Cover)> families,
        IReadOnlyList<string> relationKeys, IReadOnlyList<string> censusKeys, IReadOnlyList<string> assertionKeys,
        MachineQueryRendererSource renderer, bool scoped, bool adaptive, LuxembourgQueryExecutionResult result,
        SourceArtifactRef selectionManifest, string selectionContentSha256, SourceArtifactRef finalManifest,
        string finalContentSha256, SourceArtifactRef run, CancellationToken cancellationToken)
    {
        // Legacy calls with incomplete relations or externally supplied consolidation results keep their
        // existing typed result. The containing acquisition catalog must require this reference explicitly.
        if (families.Count == 0 || result.FamilyOutcomes.Count != families.Count ||
            families.Any(static family => family.PartitionRequest.SetId is not ("S" or "A" or "G")) ||
            !relationKeys.SequenceEqual(families.Where(static family => family.PartitionRequest.SetId == "G").Select(static family => family.PartitionRequest.Partition.PartitionId)) ||
            !censusKeys.SequenceEqual(families.Where(static family => family.PartitionRequest.SetId == "S").Select(static family => family.PartitionRequest.Partition.PartitionId)) ||
            !assertionKeys.SequenceEqual(families.Where(static family => family.PartitionRequest.SetId == "A").Select(static family => family.PartitionRequest.Partition.PartitionId)) ||
            result.FamilyOutcomes.Any(static outcome => outcome.CheckpointRef is null ||
                outcome.Kind is not (LuxembourgFamilyEnumerationOutcomeKind.Proven or LuxembourgFamilyEnumerationOutcomeKind.CoverProven)))
            return null;
        var retained = new List<QueryFamily>();
        foreach (var family in families)
        {
            var request = family.PartitionRequest;
            var outcome = result.FamilyOutcomes.Single(value => value.FamilyKey == request.Partition.PartitionId);
            var proof = outcome.Proof ?? outcome.CoverLeafProofs![0];
            var planBytes = LuxembourgQueryPlan.GetWireBytes(request.InvariantPlan);
            await HoldQueryInputAsync(planBytes, cancellationToken).ConfigureAwait(false);
            await HoldQueryInputAsync(request.RendererSource.CopyBytes(), cancellationToken).ConfigureAwait(false);
            retained.Add(new(request.SetId, request.Partition,
                LuxembourgQueryPlanIdentity.Create(request.InvariantPlanResourceId, request.InvariantPlan),
                CustodyDigest.Of(planBytes), request.RendererSource.Reference, outcome.Kind, outcome.CheckpointRef!,
                proof.AcquisitionRunRef, proof.InterpretationProfileRef));
        }
        await HoldQueryInputAsync(renderer.CopyBytes(), cancellationToken).ConfigureAwait(false);
        var document = new QueryCheckpoint(QueryCheckpointSchema, HashDocument(_sourceProfile.Snapshot), retained.ToArray(),
            renderer.Reference, scoped, adaptive, selectionManifest, selectionContentSha256, finalManifest, finalContentSha256,
            run, result.DocumentCheckpointRef!, result.GazetteCheckpointRef!, result.CorpusRecordSetRef!,
            result.ObservedObjectIdentitySetRef!, QueryResultDigest(result));
        var bytes = EncodeQuery(document);
        await HoldQueryInputAsync(bytes, cancellationToken).ConfigureAwait(false);
        return new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", CustodyDigest.Of(bytes));
    }

    private async Task<(ScopeManifest? Manifest, DurableBlobWriteReceipt? Receipt, SourceArtifactRef? ArtifactRef,
        string? CanonicalSha256, LuxembourgQueryExecutionRefusalDetail? Refusal)> HoldAcquisitionManifestAsync(
        VerifiedScopeManifest derived, IScopeReductionEvidenceResolver resolver, SourceArtifactRef? original,
        string? originalStorageSha256, CancellationToken cancellationToken)
    {
        var current = await HoldManifestAsync(derived, resolver, cancellationToken).ConfigureAwait(false);
        if (original is null || current.Refusal is not null) return current;
        if (current.ArtifactRef != original || originalStorageSha256 is null)
            throw new CustodyIntegrityException("Rederived LU manifest differs from its original canonical identity.");
        var originalBytes = await CustodyRestore.ReadByDigestCheckedAsync(_custodyStore, originalStorageSha256,
            cancellationToken).ConfigureAwait(false);
        ScopeManifest verified;
        var length = ChunkedDerivedArtifact.MeasureCanonicalBytes(
            output => ScopeManifestCanonicalWriter.Write(output, derived), cancellationToken);
        if (length > ChunkedDerivedArtifact.ChunkSize)
        {
            // The original storage root includes historical chunk receipts. Re-creating that root
            // with today's receipts changes its storage digest even when canonical bytes agree.
            // Normal HoldManifestAsync above checks current holds for every newly derived chunk;
            // this separately verifies the original root against the same derived manifest.
            var artifact = await ChunkedDerivedArtifact.OpenAsync(_custodyStore, originalStorageSha256,
                "lex-lu-scope-manifest/1", cancellationToken).ConfigureAwait(false);
            using var stream = artifact.OpenRead();
            verified = VerifiedScopeManifest.VerifyStreamAgainst(original, stream, derived, resolver).Manifest;
        }
        else
        {
            using var stream = new MemoryStream(originalBytes.ToArray(), writable: false);
            verified = VerifiedScopeManifest.VerifyStreamAgainst(original, stream, derived, resolver).Manifest;
        }
        var (receipt, failure) = await CustodyHold.TryHoldAsync(_custodyStore, originalBytes, cancellationToken).ConfigureAwait(false);
        if (receipt is null) throw new CustodyRequiredException("Original LU manifest storage root cannot be retained: " + failure);
        if (receipt.Reference.ContentSha256 != originalStorageSha256 || receipt.Reference.ByteLength != originalBytes.Length)
            throw new CustodyIntegrityException("Original LU manifest root receipt names different bytes.");
        return (verified, receipt, original, original.Sha256, null);
    }

    private async Task HoldQueryInputAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        var (receipt, failure) = await CustodyHold.TryHoldAsync(_custodyStore, bytes, cancellationToken).ConfigureAwait(false);
        if (receipt is null) throw new CustodyRequiredException("LU query catalog dependency could not be retained: " + failure);
        if (receipt.Reference.ContentSha256 != CustodyDigest.Of(bytes.Span) || receipt.Reference.ByteLength != bytes.Length)
            throw new CustodyIntegrityException("LU query catalog dependency receipt names different bytes.");
    }

    private static string[] Keys(IEnumerable<QueryFamily> families, string set) =>
        families.Where(family => family.Set == set).Select(static family => family.Range.PartitionId).ToArray();
    private static string QueryResultDigest(LuxembourgQueryExecutionResult result) => HashDocument(new
    {
        result.ScopeManifestCanonicalSha256, result.CorpusRecordSetRef, result.ObservedObjectIdentitySetRef,
        result.ResourceObservationSubjects, result.ResourceObservationExclusions, result.TypedAssertions,
        result.ResolvedRelations, result.LocalInboundRelations, result.RelationFamilyAcquisitions, result.PopulationLedger,
        result.GazetteBodySetsByOrdinal, result.GazetteListingFetchRefusalsByOrdinal,
        result.GazetteListingsWithContradictoryLegalValueByOrdinal,
    });
    private static byte[] EncodeQuery(QueryCheckpoint document) => Encoding.UTF8.GetBytes(ContractJson.Serialize(document));
    private sealed record QueryFamily(string Set, LuxembourgQueryPartitionRange Range, SourceArtifactRef Plan,
        string PlanWireSha256, SourceArtifactRef Renderer, LuxembourgFamilyEnumerationOutcomeKind Kind,
        SourceArtifactRef Checkpoint, SourceArtifactRef Run, SourceArtifactRef Profile);
    private sealed record QueryCheckpoint(string Schema, string ProfileSha256, QueryFamily[] Families,
        SourceArtifactRef DocumentRenderer, bool Scoped, bool Adaptive, SourceArtifactRef SelectionManifest,
        string SelectionContentSha256, SourceArtifactRef FinalManifest, string FinalContentSha256,
        SourceArtifactRef Run, SourceArtifactRef Documents, SourceArtifactRef Gazette,
        SourceArtifactRef Corpus, SourceArtifactRef Observed, string ResultSha256);
    private sealed class QueryReplay(QueryCheckpoint document)
    {
        internal QueryCheckpoint Document { get; } = document;
        internal Dictionary<string, List<FamilyRowsLeg>> Legs { get; } = new(StringComparer.Ordinal);
    }
}
