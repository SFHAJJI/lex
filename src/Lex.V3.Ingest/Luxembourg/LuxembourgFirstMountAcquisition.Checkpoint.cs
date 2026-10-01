using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;

namespace Lex.V3.Ingest.Luxembourg;

public sealed partial class LuxembourgFirstMountAcquisition
{
    private const string AcquisitionCheckpointSchema = "lex-lu-first-mount-acquisition/1";

    /// <summary>Restores vocabulary, query acquisition and AKN derivation from checked custody without HTTP.</summary>
    public static async Task<LuxembourgFirstMountAcquisitionResult> ReopenAsync(ICustodyStore store,
        SourceArtifactRef checkpoint, LuxembourgActRange expectedAct, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(expectedAct);
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, cancellationToken).ConfigureAwait(false);
        try
        {
            var document = ContractJson.Deserialize<AcquisitionCheckpoint>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (document is null || document.Schema != AcquisitionCheckpointSchema || document.Act != expectedAct ||
                !bytes.Span.SequenceEqual(EncodeAcquisition(document)) || document.Vocabulary is null ||
                document.Observation is null || document.Query is null || document.Corpus is null || document.Observed is null ||
                document.QueryRenderer is null || document.DocumentRenderer is null)
                throw new CustodyIntegrityException("LU acquisition catalog framing or intended act differs.");
            return await RestorePartsAsync(store, checkpoint, new AcquisitionParts(document.Vocabulary, document.Observation, document.Query, document.QueryRenderer, document.DocumentRenderer, document.Corpus, document.Observed, document.InventorySha256, document.ContentSha256),
                LuxembourgActRange.Families.Select(expectedAct.FamilyRange).ToArray(), null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("LU acquisition catalog failed independent verification.", exception);
        }
    }

    public static async Task<LuxembourgFirstMountAcquisitionResult> ReopenPopulationAsync(ICustodyStore store,
        SourceArtifactRef checkpoint, LuxembourgPopulationScope expectedScope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(expectedScope);
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, cancellationToken).ConfigureAwait(false);
        try
        {
            var document = ContractJson.Deserialize<PopulationCheckpoint>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (document is null || document.Schema != "lex-lu-first-mount-acquisition/2" ||
                document.ScopeDefinition is null || document.ScopeManifest is null || document.Ranges is null || document.Parts is null ||
                !bytes.Span.SequenceEqual(Encoding.UTF8.GetBytes(ContractJson.Serialize(document))))
                throw new CustodyIntegrityException("LU population catalog framing is invalid.");
            var declared = new LuxembourgPopulationScope(document.Policy, document.Ranges);
            if (!declared.SameScope(expectedScope))
                throw new CustodyIntegrityException("LU population catalog differs from the intended scope.");
            var scopeBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, document.ScopeDefinition.Sha256,
                cancellationToken).ConfigureAwait(false);
            if (!scopeBytes.Span.SequenceEqual(declared.DeclarationBytes()))
                throw new CustodyIntegrityException("LU population scope declaration differs from its ranges or policy.");
            var restored = await RestorePartsAsync(store, checkpoint, document.Parts,
                declared.Ranges.SelectMany(range => LuxembourgActRange.Families.Select(range.FamilyRange)).ToArray(),
                document.ScopeDefinition, cancellationToken).ConfigureAwait(false);
            var manifestBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, document.ScopeManifest.Sha256,
                cancellationToken).ConfigureAwait(false);
            if (!manifestBytes.Span.SequenceEqual(declared.ManifestBytes(restored.Profile!, document.ScopeDefinition, restored.VocabularyEvidenceRef!)))
                throw new CustodyIntegrityException("LU population family dispositions differ from the checked vocabulary or scope.");
            return restored.WithPopulationScopeManifest(document.ScopeManifest);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("LU population catalog failed independent verification.", exception);
        }
    }

    private static async Task<LuxembourgFirstMountAcquisitionResult> RestorePartsAsync(ICustodyStore store,
        SourceArtifactRef checkpoint, AcquisitionParts parts,
        IReadOnlyList<Lex.V3.Contracts.Source.Luxembourg.LuxembourgQueryPartitionRange> ranges,
        SourceArtifactRef? expectedScopeDefinition, CancellationToken cancellationToken)
    {
        if (parts.Vocabulary is null || parts.Observation is null || parts.Query is null ||
            parts.QueryRenderer is null || parts.DocumentRenderer is null || parts.Corpus is null || parts.Observed is null)
            throw new CustodyIntegrityException("LU acquisition catalog has missing phase references.");
        var queryBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, parts.QueryRenderer.Sha256, cancellationToken).ConfigureAwait(false);
        var documentBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, parts.DocumentRenderer.Sha256, cancellationToken).ConfigureAwait(false);
        var renderers = new LuxembourgRendererSources(MachineQueryRendererSource.Open(parts.QueryRenderer, queryBytes.Span),
            MachineQueryRendererSource.Open(parts.DocumentRenderer, documentBytes.Span));
        var profile = await ReopenVocabularyAsync(store, parts.Vocabulary, parts.Observation,
            renderers.Query, cancellationToken).ConfigureAwait(false);
        var run = await LuxembourgQueryExecutionAdapter.ReopenAcquisitionAsync(store, parts.Query, profile,
            ranges, cancellationToken, renderers, expectedScopeDefinition).ConfigureAwait(false);
        if (run.Refusal is not null || run.CorpusRecordSetRef != parts.Corpus || run.ObservedObjectIdentitySetRef != parts.Observed ||
            run.HeldBodyDerivationPopulation is not { } population)
            throw new CustodyIntegrityException("LU query belongs to a different original acquisition.");
        var inventory = await new LuxembourgAknArticleInventoryProducer(store).RunAsync(population, cancellationToken).ConfigureAwait(false);
        var content = await new LuxembourgAknLegalContentProfileProducer(store).RunAsync(inventory, cancellationToken).ConfigureAwait(false);
        if (inventory.IdentitySha256 != parts.InventorySha256 || content.IdentitySha256 != parts.ContentSha256)
            throw new CustodyIntegrityException("LU AKN derivation differs from the original acquisition.");
        return LuxembourgFirstMountAcquisitionResult.Success(run, profile, parts.Observation, inventory, content)
            .WithVocabularyCheckpoint(parts.Vocabulary).WithCheckpoint(checkpoint);
    }

    private async Task<SourceArtifactRef> RetainAcquisitionCheckpointAsync(LuxembourgPopulationScope scope, bool preserveLegacy,
        SourceArtifactRef scopeDefinition, LuxembourgRendererSources renderers, LuxembourgFirstMountAcquisitionResult result, CancellationToken cancellationToken)
    {
        if (result.Run?.AcquisitionCheckpointRef is not { } query || result.VocabularyCheckpointRef is not { } vocabulary ||
            result.VocabularyEvidenceRef is not { } observation || result.Run.CorpusRecordSetRef is not { } corpus ||
            result.Run.ObservedObjectIdentitySetRef is not { } observed || result.AknInventory is not { } inventory ||
            result.AknLegalContent is not { } content)
            throw new CustodyRequiredException("A complete LU acquisition requires all of its retained phase checkpoints.");
        foreach (var renderer in new[] { renderers.Query, renderers.DocumentFetch })
            await HoldAcquisitionBytesAsync(renderer.CopyBytes(), cancellationToken).ConfigureAwait(false);
        foreach (var reference in new[] { query, vocabulary })
            _ = await CustodyRestore.ReadByDigestCheckedAsync(_custodyStore, reference.Sha256, cancellationToken).ConfigureAwait(false);
        var parts = new AcquisitionParts(vocabulary, observation, query,
            renderers.Query.Reference, renderers.DocumentFetch.Reference, corpus, observed,
            inventory.IdentitySha256, content.IdentitySha256);
        var bytes = preserveLegacy
            ? EncodeAcquisition(new AcquisitionCheckpoint(AcquisitionCheckpointSchema, scope.Ranges.Single(),
                parts.Vocabulary, parts.Observation, parts.Query, parts.QueryRenderer, parts.DocumentRenderer, parts.Corpus, parts.Observed, parts.InventorySha256, parts.ContentSha256))
            : Encoding.UTF8.GetBytes(ContractJson.Serialize(new PopulationCheckpoint(
                "lex-lu-first-mount-acquisition/2", scope.Policy, scope.Ranges.ToArray(), scopeDefinition,
                result.PopulationScopeManifestRef ?? throw new CustodyRequiredException("Population family dispositions are not retained."), parts)));
        await HoldAcquisitionBytesAsync(bytes, cancellationToken).ConfigureAwait(false);
        return new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", CustodyDigest.Of(bytes));
    }

    private async Task HoldAcquisitionBytesAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        var (receipt, failure) = await CustodyHold.TryHoldAsync(_custodyStore, bytes, cancellationToken).ConfigureAwait(false);
        if (receipt is null) throw new CustodyRequiredException("LU acquisition input cannot be retained: " + failure);
        if (receipt.Reference.ContentSha256 != CustodyDigest.Of(bytes.Span) || receipt.Reference.ByteLength != bytes.Length)
            throw new CustodyIntegrityException("LU acquisition input receipt names different bytes.");
    }
    private static byte[] EncodeAcquisition(AcquisitionCheckpoint document) => Encoding.UTF8.GetBytes(ContractJson.Serialize(document));
    private sealed record AcquisitionCheckpoint(string Schema, LuxembourgActRange Act, SourceArtifactRef Vocabulary,
        SourceArtifactRef Observation, SourceArtifactRef Query, SourceArtifactRef QueryRenderer, SourceArtifactRef DocumentRenderer,
        SourceArtifactRef Corpus, SourceArtifactRef Observed, string InventorySha256, string ContentSha256);
    private sealed record AcquisitionParts(SourceArtifactRef Vocabulary, SourceArtifactRef Observation,
        SourceArtifactRef Query, SourceArtifactRef QueryRenderer, SourceArtifactRef DocumentRenderer,
        SourceArtifactRef Corpus, SourceArtifactRef Observed, string InventorySha256, string ContentSha256);
    private sealed record PopulationCheckpoint(string Schema, string Policy, LuxembourgActRange[] Ranges,
        SourceArtifactRef ScopeDefinition, SourceArtifactRef ScopeManifest, AcquisitionParts Parts);

}
