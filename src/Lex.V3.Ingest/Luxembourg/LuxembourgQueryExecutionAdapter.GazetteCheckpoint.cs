using GazetteAcquisitionData = (System.Collections.Generic.IReadOnlyDictionary<int, Lex.V3.Contracts.Source.Luxembourg.LuxembourgGazetteBodySet>? Sets, System.Collections.Generic.IReadOnlyDictionary<int, System.Collections.Generic.IReadOnlyDictionary<string, Lex.V3.Contracts.Source.Corpus.CorpusAcquisitionRefusalReason>>? FetchRefusals, System.Collections.Generic.IReadOnlyDictionary<int, System.Collections.Generic.IReadOnlyList<string>>? ContradictoryLegalValues, Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionRefusalDetail? Refusal);
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Http;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Scope;

namespace Lex.V3.Ingest.Luxembourg;

public sealed partial class LuxembourgQueryExecutionAdapter
{
    private const string GazetteCheckpointSchema = "lex-lu-gazette-checkpoint/1";

    internal async Task<GazetteAcquisitionData> RunGazetteAcquisitionAsync(LuxembourgProfileResolution.Resolved resolved,
        ScopeManifest manifest, IReadOnlyDictionary<SourceObjectRef, LuxembourgDocumentFetchAddress> addresses,
        IReadOnlyDictionary<int, RoutedHttpEvidence> held, MachineQueryRendererSource renderer,
        WireRequestBudget budget, CancellationToken cancellationToken) =>
        (await RunGazetteAcquisitionWithCheckpointAsync(resolved, manifest, addresses, held,
            renderer, budget, cancellationToken).ConfigureAwait(false)).Data;

    internal async Task<(GazetteAcquisitionData Data, SourceArtifactRef? Checkpoint)> RunGazetteAcquisitionWithCheckpointAsync(
        LuxembourgProfileResolution.Resolved resolved, ScopeManifest manifest,
        IReadOnlyDictionary<SourceObjectRef, LuxembourgDocumentFetchAddress> addresses,
        IReadOnlyDictionary<int, RoutedHttpEvidence> held, MachineQueryRendererSource renderer,
        WireRequestBudget budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentNullException.ThrowIfNull(held);
        var addressSnapshot = addresses.ToDictionary();
        var heldSnapshot = held.ToDictionary();
        // The Gazette selection's input digest names the document phase's held routes, so a resume replays Gazette GETs
        // only after a document phase that held the same routes.
        var capture = Progress is null ? new DocumentReplay(null)
            : new DocumentReplay(null, Progress, AcquisitionJournal.LuxembourgGazettePhase,
                GazetteInputDigest(resolved, manifest, addressSnapshot, heldSnapshot));
        var result = await RunGazetteAcquisitionCoreAsync(resolved, manifest, addressSnapshot, heldSnapshot,
            renderer, budget, cancellationToken, capture).ConfigureAwait(false);
        if (result.Refusal is not null) return (result, null);
        try
        {
            var document = new GazetteCheckpoint(GazetteCheckpointSchema,
                GazetteInputDigest(resolved, manifest, addressSnapshot, heldSnapshot), renderer.Reference,
                capture.Fetches.ToArray(), GazetteResultDigest(result));
            var bytes = EncodeGazette(document);
            var (receipt, failure) = await CustodyHold.TryHoldAsync(_custodyStore, bytes, cancellationToken).ConfigureAwait(false);
            if (receipt is null) throw new CustodyRequiredException("Gazette checkpoint hold refused: " + failure);
            if (receipt.Reference.ContentSha256 != CustodyDigest.Of(bytes) || receipt.Reference.ByteLength != bytes.Length)
                throw new CustodyIntegrityException("Gazette checkpoint receipt names different bytes.");
            return (result, new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", receipt.Reference.ContentSha256));
        }
        catch (Exception exception) when (exception is CustodyRequiredException or CustodyIntegrityException)
        {
            return ((null, null, null, new LuxembourgQueryExecutionRefusalDetail(
                LuxembourgQueryExecutionRefusal.GazetteCheckpointNotRetained, null, exception.Message)), null);
        }
    }

    /// <summary>Replays Gazette-only requests and preserves routes already checked by the document phase.</summary>
    /// <remarks>The caller must rederive the resolution/manifest and supply the original verified held-route map.</remarks>
    internal static async Task<GazetteAcquisitionData> ReopenGazetteAcquisitionAsync(ICustodyStore store,
        SourceArtifactRef checkpoint, VerifiedLuxembourgSourceProfile profile, LuxembourgProfileResolution.Resolved resolved,
        ScopeManifest manifest, IReadOnlyDictionary<SourceObjectRef, LuxembourgDocumentFetchAddress> addresses,
        IReadOnlyDictionary<int, RoutedHttpEvidence> held, MachineQueryRendererSource renderer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(renderer);
        cancellationToken.ThrowIfCancellationRequested();
        var addressSnapshot = addresses.ToDictionary();
        var heldSnapshot = held.ToDictionary();
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, cancellationToken).ConfigureAwait(false);
        try
        {
            var document = ContractJson.Deserialize<GazetteCheckpoint>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (document.Schema != GazetteCheckpointSchema || !bytes.Span.SequenceEqual(EncodeGazette(document)) ||
                document.Renderer != renderer.Reference || document.InputSha256 != GazetteInputDigest(resolved, manifest, addressSnapshot, heldSnapshot) ||
                document.Fetches is null || document.Fetches.Any(static fetch => fetch is null || fetch.Ordinal < 0))
                throw new CustodyIntegrityException("Gazette checkpoint framing or independently selected inputs disagree.");
            var replay = new DocumentReplay(new DocumentCheckpoint(DocumentCheckpointSchema,
                document.InputSha256, document.Renderer, document.Fetches, document.ResultSha256));
            var budget = WireRequestBudget.OfWireRequests(2);
            var adapter = new LuxembourgQueryExecutionAdapter(store,
                new LuxembourgRepeatedEnumerationExecutor(store, TimeProvider.System), profile);
            var result = await adapter.RunGazetteAcquisitionCoreAsync(resolved, manifest, addressSnapshot, heldSnapshot,
                renderer, budget, cancellationToken, replay).ConfigureAwait(false);
            replay.RequireEnd();
            if (result.Refusal is not null || budget.Spent != 0 || GazetteResultDigest(result) != document.ResultSha256)
                throw new CustodyIntegrityException("Gazette replay differs from original sets or listing dispositions: " + result.Refusal?.Detail);
            return result;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("Gazette checkpoint failed independent verification.", exception);
        }
    }

    private static string GazetteInputDigest(LuxembourgProfileResolution.Resolved resolved, ScopeManifest manifest,
        IReadOnlyDictionary<SourceObjectRef, LuxembourgDocumentFetchAddress> addresses,
        IReadOnlyDictionary<int, RoutedHttpEvidence> held) => HashDocument(new
    {
        Manifest = manifest,
        Resources = resolved.Resources.Select(static resource => new
        {
            resource.ObjectRef, resource.PublicationForm, resource.BodyJoin,
            Assertions = resource.Assertions.Select(static value => value.Assertion).ToArray(),
        }).ToArray(),
        Addresses = addresses.OrderBy(static pair => pair.Key.CanonicalKey, StringComparer.Ordinal)
            .Select(static pair => new { Object = pair.Key.CanonicalKey, Address = pair.Value.ArtifactRef }).ToArray(),
        Held = held.OrderBy(static pair => pair.Key).Select(static pair => new
        {
            Ordinal = pair.Key, RouteSha256 = CustodyDigest.Of(pair.Value.CopyCanonicalBytes()),
        }).ToArray(),
    });
    private static string GazetteResultDigest(GazetteAcquisitionData result) => HashDocument(new
    {
        Sets = result.Sets!.OrderBy(static pair => pair.Key).ToArray(),
        Refusals = result.FetchRefusals!.OrderBy(static pair => pair.Key).Select(static pair => new
        {
            Ordinal = pair.Key, Items = pair.Value.OrderBy(static value => value.Key, StringComparer.Ordinal).ToArray(),
        }).ToArray(),
        Contradictions = result.ContradictoryLegalValues!.OrderBy(static pair => pair.Key).ToArray(),
    });
    private static byte[] EncodeGazette(GazetteCheckpoint value) => Encoding.UTF8.GetBytes(ContractJson.Serialize(value));
    private sealed record GazetteCheckpoint(string Schema, string InputSha256, SourceArtifactRef Renderer,
        DocumentFetch[] Fetches, string ResultSha256);
}
