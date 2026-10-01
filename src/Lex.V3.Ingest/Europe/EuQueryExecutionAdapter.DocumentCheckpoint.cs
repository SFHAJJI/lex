using DocumentAcquisitionData = (System.Collections.Generic.IReadOnlyDictionary<int, Lex.V3.Ingest.CorpusAcquisitionOutcome>? Outcomes, System.Collections.Generic.IReadOnlyDictionary<int, Lex.V3.Ingest.Europe.EuDocumentLadderResult>? LadderResults, System.Collections.Generic.IReadOnlyDictionary<int, Lex.V3.Ingest.Europe.EuMintedRowAccounting>? MintedRows, Lex.V3.Ingest.Europe.EuQueryExecutionRefusalDetail? Refusal);
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Ingest.Europe;

public sealed partial class EuQueryExecutionAdapter
{
    private const string DocumentCheckpointSchema = "lex-eu-document-ladders-checkpoint/1";

    internal async Task<DocumentAcquisitionData> RunDocumentAcquisitionAsync(ScopeManifest reopenedManifest,
        IReadOnlyDictionary<SourceObjectRef, IReadOnlyList<EuDocumentFetchAddress>> mintedAddressesByObjectRef,
        MachineQueryRendererSource documentFetchRendererSource, BoundMachineRequest documentFetchSourceWitness,
        WireRequestBudget wireBudget, CancellationToken cancellationToken) =>
        (await RunDocumentAcquisitionWithCheckpointAsync(reopenedManifest, mintedAddressesByObjectRef,
            documentFetchRendererSource, documentFetchSourceWitness, wireBudget, cancellationToken).ConfigureAwait(false)).Data;

    internal async Task<(DocumentAcquisitionData Data, SourceArtifactRef? Checkpoint)> RunDocumentAcquisitionWithCheckpointAsync(
        ScopeManifest manifest, IReadOnlyDictionary<SourceObjectRef, IReadOnlyList<EuDocumentFetchAddress>> addresses,
        MachineQueryRendererSource renderer, BoundMachineRequest witness, WireRequestBudget budget, CancellationToken cancellationToken)
    {
        var snapshot = SnapshotAddresses(addresses);
        var capture = new DocumentLadderReplay(null);
        var result = await RunDocumentAcquisitionCoreAsync(manifest, snapshot, renderer, witness, budget, cancellationToken, capture).ConfigureAwait(false);
        if (result.Refusal is not null) return (result, null);
        try
        {
            var document = new DocumentLadderCheckpoint(DocumentCheckpointSchema, DocumentInputDigest(manifest, snapshot),
                renderer.Reference, capture.Fetches.ToArray(), DocumentResultDigest(result));
            var bytes = EncodeDocumentCheckpoint(document);
            var (receipt, failure) = await CustodyHold.TryHoldAsync(_custodyStore, bytes, cancellationToken).ConfigureAwait(false);
            if (receipt is null) throw new CustodyRequiredException("Document ladder checkpoint hold refused: " + failure);
            if (receipt.Reference.ContentSha256 != CustodyDigest.Of(bytes) || receipt.Reference.ByteLength != bytes.Length)
                throw new CustodyIntegrityException("Document ladder checkpoint receipt names different bytes.");
            return (result, new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", receipt.Reference.ContentSha256));
        }
        catch (Exception exception) when (exception is CustodyRequiredException or CustodyIntegrityException)
        {
            return ((null, null, null, new EuQueryExecutionRefusalDetail(EuQueryExecutionRefusal.DocumentCheckpointNotRetained, exception.Message)), null);
        }
    }

    internal static async Task<DocumentAcquisitionData> ReopenDocumentAcquisitionAsync(ICustodyStore store,
        SourceArtifactRef checkpoint, ScopeManifest manifest,
        IReadOnlyDictionary<SourceObjectRef, IReadOnlyList<EuDocumentFetchAddress>> addresses,
        MachineQueryRendererSource renderer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(renderer);
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = SnapshotAddresses(addresses);
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, cancellationToken).ConfigureAwait(false);
        try
        {
            var document = ContractJson.Deserialize<DocumentLadderCheckpoint>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (document.Schema != DocumentCheckpointSchema || !bytes.Span.SequenceEqual(EncodeDocumentCheckpoint(document)) ||
                document.Renderer != renderer.Reference || document.InputSha256 != DocumentInputDigest(manifest, snapshot) ||
                document.Fetches is null || document.Fetches.Any(static value => value is null || value.Ordinal < 0))
                throw new CustodyIntegrityException("Document ladder checkpoint framing or caller binding disagrees.");
            var replay = new DocumentLadderReplay(document);
            var budget = WireRequestBudget.OfWireRequests(2);
            var result = await new EuQueryExecutionAdapter(store, new EuRepeatedEnumerationExecutor(store, TimeProvider.System))
                .RunDocumentAcquisitionCoreAsync(manifest, snapshot, renderer, null, budget, cancellationToken, replay).ConfigureAwait(false);
            replay.RequireEnd();
            if (result.Refusal is not null || budget.Spent != 0 || DocumentResultDigest(result) != document.ResultSha256)
                throw new CustodyIntegrityException("Restored document ladders differ from the original outcomes: " + result.Refusal?.Detail);
            return result;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("Document ladder checkpoint failed independent verification.", exception);
        }
    }

    private static IReadOnlyDictionary<SourceObjectRef, IReadOnlyList<EuDocumentFetchAddress>> SnapshotAddresses(
        IReadOnlyDictionary<SourceObjectRef, IReadOnlyList<EuDocumentFetchAddress>> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        return addresses.ToDictionary(static pair => pair.Key,
            static pair => (IReadOnlyList<EuDocumentFetchAddress>)Array.AsReadOnly(pair.Value.ToArray()));
    }
    private static string DocumentInputDigest(ScopeManifest manifest,
        IReadOnlyDictionary<SourceObjectRef, IReadOnlyList<EuDocumentFetchAddress>> addresses) => HashDocument(new
    {
        Manifest = manifest,
        Addresses = addresses.OrderBy(static pair => pair.Key.CanonicalKey, StringComparer.Ordinal).Select(static pair => new
        {
            Object = pair.Key.CanonicalKey,
            Ladder = pair.Value.Select(static value => new { value.ResourceUri, value.Accept, value.AcceptLanguage, value.MediaType }).ToArray(),
        }).ToArray(),
    });
    private static string DocumentResultDigest(DocumentAcquisitionData result) => HashDocument(new
    {
        Outcomes = result.Outcomes!.OrderBy(static pair => pair.Key).Select(static pair => new
        {
            Ordinal = pair.Key, pair.Value.Refusal, BodySha256 = pair.Value.Receipt?.Reference.ContentSha256,
            BodyLength = pair.Value.Receipt?.Reference.ByteLength,
        }).ToArray(),
        Ladders = result.LadderResults!.OrderBy(static pair => pair.Key).ToArray(),
        Minted = result.MintedRows!.OrderBy(static pair => pair.Key).ToArray(),
    });
    private static string HashDocument<T>(T value) => CustodyDigest.Of(Encoding.UTF8.GetBytes(ContractJson.Serialize(value)));
    private static byte[] EncodeDocumentCheckpoint(DocumentLadderCheckpoint value) => Encoding.UTF8.GetBytes(ContractJson.Serialize(value));
    private sealed record DocumentLadderCheckpoint(string Schema, string InputSha256, SourceArtifactRef Renderer,
        DocumentLadderFetch[] Fetches, string ResultSha256);
    private sealed record DocumentLadderFetch(int Ordinal, string Uri, string Accept, string Language,
        SourceArtifactRef? Route, SourceArtifactRef? Run, string? RequestSha256, EuDocumentFetchAttemptRefusal? Refusal, string? Detail);

    private sealed class DocumentLadderReplay(DocumentLadderCheckpoint? document)
    {
        private int _index;
        internal bool IsReplay => document is not null;
        internal List<DocumentLadderFetch> Fetches { get; } = [];
        internal void Capture(int ordinal, EuDocumentFetchAddress address, EuDocumentFetchAttemptResult attempt)
        {
            var route = attempt.Evidence;
            Fetches.Add(new DocumentLadderFetch(ordinal, address.ResourceUri, address.Accept, address.AcceptLanguage,
                route is null ? null : new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", CustodyDigest.Of(route.CopyCanonicalBytes())),
                route?.RunIdentity, route?.Hops[0].LogicalRequestSha256, attempt.Refusal, attempt.Detail));
        }
        internal async Task<EuDocumentFetchAttemptResult> FetchAsync(ICustodyStore store, int ordinal,
            EuDocumentFetchAddress address, CancellationToken cancellationToken)
        {
            if (document is null || _index >= document.Fetches.Length)
                throw new CustodyIntegrityException("A selected document ladder requires a missing fetch.");
            var fetch = document.Fetches[_index++];
            if (fetch.Ordinal != ordinal || fetch.Uri != address.ResourceUri || fetch.Accept != address.Accept || fetch.Language != address.AcceptLanguage)
                throw new CustodyIntegrityException("Document fetch differs from the selected row or ordered representation.");
            if (fetch.Route is null)
            {
                if (fetch.Run is not null || fetch.RequestSha256 is not null || fetch.Refusal is null ||
                    fetch.Refusal == EuDocumentFetchAttemptRefusal.None || !Enum.IsDefined(fetch.Refusal.Value))
                    throw new CustodyIntegrityException("Non-executed document attempt has inconsistent evidence.");
                return EuDocumentFetchAttemptResult.Refused(fetch.Refusal.Value, fetch.Detail);
            }
            if (fetch.Run is null || fetch.RequestSha256 is null || fetch.Refusal is not null || fetch.Detail is not null)
                throw new CustodyIntegrityException("Executed document attempt has inconsistent evidence.");
            return await EuDocumentFetchRouteReader.ReopenAsync(store, fetch.Route, fetch.Run, fetch.RequestSha256, address, cancellationToken).ConfigureAwait(false);
        }
        internal void RequireEnd()
        {
            if (document is null || _index != document.Fetches.Length)
                throw new CustodyIntegrityException("Document checkpoint contains unconsumed fetches.");
        }
    }
}
