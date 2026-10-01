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
            var queryBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, document.QueryRenderer.Sha256, cancellationToken).ConfigureAwait(false);
            var documentBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, document.DocumentRenderer.Sha256, cancellationToken).ConfigureAwait(false);
            var renderers = new LuxembourgRendererSources(MachineQueryRendererSource.Open(document.QueryRenderer, queryBytes.Span),
                MachineQueryRendererSource.Open(document.DocumentRenderer, documentBytes.Span));
            var profile = await ReopenVocabularyAsync(store, document.Vocabulary, document.Observation,
                renderers.Query, cancellationToken).ConfigureAwait(false);
            var run = await LuxembourgQueryExecutionAdapter.ReopenAcquisitionAsync(store, document.Query, profile,
                LuxembourgActRange.Families.Select(expectedAct.FamilyRange).ToArray(), cancellationToken, renderers).ConfigureAwait(false);
            if (run.Refusal is not null || run.CorpusRecordSetRef != document.Corpus || run.ObservedObjectIdentitySetRef != document.Observed ||
                run.HeldBodyDerivationPopulation is not { } population)
                throw new CustodyIntegrityException("LU query belongs to a different original acquisition.");
            var inventory = await new LuxembourgAknArticleInventoryProducer(store).RunAsync(population, cancellationToken).ConfigureAwait(false);
            var content = await new LuxembourgAknLegalContentProfileProducer(store).RunAsync(inventory, cancellationToken).ConfigureAwait(false);
            if (inventory.IdentitySha256 != document.InventorySha256 || content.IdentitySha256 != document.ContentSha256)
                throw new CustodyIntegrityException("LU AKN derivation differs from the original acquisition.");
            return LuxembourgFirstMountAcquisitionResult.Success(run, profile, document.Observation, inventory, content)
                .WithVocabularyCheckpoint(document.Vocabulary).WithCheckpoint(checkpoint);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("LU acquisition catalog failed independent verification.", exception);
        }
    }

    private async Task<SourceArtifactRef> RetainAcquisitionCheckpointAsync(LuxembourgActRange act,
        LuxembourgRendererSources renderers, LuxembourgFirstMountAcquisitionResult result, CancellationToken cancellationToken)
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
        var document = new AcquisitionCheckpoint(AcquisitionCheckpointSchema, act, vocabulary, observation, query,
            renderers.Query.Reference, renderers.DocumentFetch.Reference, corpus, observed,
            inventory.IdentitySha256, content.IdentitySha256);
        var bytes = EncodeAcquisition(document);
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
}
