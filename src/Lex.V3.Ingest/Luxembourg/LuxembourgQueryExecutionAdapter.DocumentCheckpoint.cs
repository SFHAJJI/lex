using DocumentAcquisitionData = (System.Collections.Generic.IReadOnlyDictionary<int, Lex.V3.Ingest.CorpusAcquisitionOutcome>? Outcomes, System.Collections.Generic.IReadOnlyDictionary<int, Lex.V3.Contracts.Source.Http.RoutedHttpEvidence>? HeldEvidenceByOrdinal, Lex.V3.Ingest.Luxembourg.LuxembourgQueryExecutionRefusalDetail? Refusal);
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
    private const string DocumentCheckpointSchema = "lex-lu-selected-documents-checkpoint/1";

    internal async Task<DocumentAcquisitionData> RunDocumentAcquisitionAsync(ScopeManifest manifest,
        IReadOnlyDictionary<SourceObjectRef, LuxembourgDocumentFetchAddress> addresses,
        MachineQueryRendererSource renderer, WireRequestBudget budget, CancellationToken cancellationToken) =>
        (await RunDocumentAcquisitionWithCheckpointAsync(manifest, addresses, renderer, budget, cancellationToken).ConfigureAwait(false)).Data;

    internal async Task<(DocumentAcquisitionData Data, SourceArtifactRef? Checkpoint)> RunDocumentAcquisitionWithCheckpointAsync(
        ScopeManifest manifest, IReadOnlyDictionary<SourceObjectRef, LuxembourgDocumentFetchAddress> addresses,
        MachineQueryRendererSource renderer, WireRequestBudget budget, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var snapshot = addresses.ToDictionary();
        var capture = new DocumentReplay(null);
        var result = await RunDocumentAcquisitionCoreAsync(manifest, snapshot, renderer, budget, cancellationToken, capture).ConfigureAwait(false);
        if (result.Refusal is not null) return (result, null);
        try
        {
            var document = new DocumentCheckpoint(DocumentCheckpointSchema, DocumentInputDigest(manifest, snapshot),
                renderer.Reference, capture.Fetches.ToArray(), DocumentResultDigest(result));
            var bytes = EncodeDocument(document);
            var (receipt, failure) = await CustodyHold.TryHoldAsync(_custodyStore, bytes, cancellationToken).ConfigureAwait(false);
            if (receipt is null) throw new CustodyRequiredException("Document checkpoint hold refused: " + failure);
            if (receipt.Reference.ContentSha256 != CustodyDigest.Of(bytes) || receipt.Reference.ByteLength != bytes.Length)
                throw new CustodyIntegrityException("Document checkpoint receipt names different bytes.");
            return (result, new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", receipt.Reference.ContentSha256));
        }
        catch (Exception exception) when (exception is CustodyRequiredException or CustodyIntegrityException)
        {
            return ((null, null, new LuxembourgQueryExecutionRefusalDetail(
                LuxembourgQueryExecutionRefusal.DocumentCheckpointNotRetained, null, exception.Message)), null);
        }
    }

    /// <summary>Repeats selected-row acquisition logic from the original requests and retained routes.</summary>
    /// <remarks>Current body holds still run. Captured robots refusals stay refusals; no fresh robots verdict is asserted.</remarks>
    internal static async Task<DocumentAcquisitionData> ReopenDocumentAcquisitionAsync(ICustodyStore store,
        SourceArtifactRef checkpoint, VerifiedLuxembourgSourceProfile profile, ScopeManifest manifest,
        IReadOnlyDictionary<SourceObjectRef, LuxembourgDocumentFetchAddress> addresses,
        MachineQueryRendererSource renderer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentNullException.ThrowIfNull(renderer);
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = addresses.ToDictionary();
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, cancellationToken).ConfigureAwait(false);
        try
        {
            var document = ContractJson.Deserialize<DocumentCheckpoint>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (document.Schema != DocumentCheckpointSchema || !bytes.Span.SequenceEqual(EncodeDocument(document)) ||
                document.InputSha256 != DocumentInputDigest(manifest, snapshot) || document.Renderer != renderer.Reference ||
                document.Fetches is null || document.Fetches.Any(static fetch => fetch is null || fetch.Ordinal < 0))
                throw new CustodyIntegrityException("Document checkpoint framing or selected inputs disagree.");
            var replay = new DocumentReplay(document);
            var budget = WireRequestBudget.OfWireRequests(2);
            var adapter = new LuxembourgQueryExecutionAdapter(store,
                new LuxembourgRepeatedEnumerationExecutor(store, TimeProvider.System), profile);
            var result = await adapter.RunDocumentAcquisitionCoreAsync(manifest, snapshot, renderer,
                budget, cancellationToken, replay).ConfigureAwait(false);
            replay.RequireEnd();
            if (result.Refusal is not null || budget.Spent != 0 || DocumentResultDigest(result) != document.ResultSha256)
                throw new CustodyIntegrityException("Document replay differs from the original selected outcomes: " + result.Refusal?.Detail);
            return result;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("Document checkpoint failed independent verification.", exception);
        }
    }

    private static string DocumentInputDigest(ScopeManifest manifest,
        IReadOnlyDictionary<SourceObjectRef, LuxembourgDocumentFetchAddress> addresses) => HashDocument(new
    {
        Manifest = manifest,
        Addresses = addresses.OrderBy(static pair => pair.Key.CanonicalKey, StringComparer.Ordinal)
            .Select(static pair => new { Object = pair.Key.CanonicalKey, Address = pair.Value.ArtifactRef }).ToArray(),
    });
    private static string DocumentResultDigest(DocumentAcquisitionData result) => HashDocument(new
    {
        Outcomes = result.Outcomes!.OrderBy(static pair => pair.Key).Select(static pair => new
        {
            Ordinal = pair.Key, pair.Value.Refusal, BodySha256 = pair.Value.Receipt?.Reference.ContentSha256,
            BodyLength = pair.Value.Receipt?.Reference.ByteLength,
        }).ToArray(),
        HeldRoutes = result.HeldEvidenceByOrdinal!.OrderBy(static pair => pair.Key).Select(static pair => new
        {
            Ordinal = pair.Key, RouteSha256 = CustodyDigest.Of(pair.Value.CopyCanonicalBytes()),
        }).ToArray(),
    });
    private static string HashDocument<T>(T value) => CustodyDigest.Of(Encoding.UTF8.GetBytes(ContractJson.Serialize(value)));
    private static byte[] EncodeDocument(DocumentCheckpoint value) => Encoding.UTF8.GetBytes(ContractJson.Serialize(value));
    private sealed record DocumentCheckpoint(string Schema, string InputSha256, SourceArtifactRef Renderer,
        DocumentFetch[] Fetches, string ResultSha256);
    private sealed record DocumentFetch(int Ordinal, SourceArtifactRef Address, string PlanId, string InputId,
        SourceArtifactRef? Route, SourceArtifactRef? Run, string? RequestSha256,
        LuxembourgDocumentGetAttemptRefusal? Refusal, string? DeniedRobotsPath, string? Detail);

    private sealed class DocumentReplay(DocumentCheckpoint? document)
    {
        private int _index;
        internal List<DocumentFetch> Fetches { get; } = [];
        internal async Task<LuxembourgDocumentGetAttemptResult> FetchAsync(ICustodyStore store,
            LuxembourgRepeatedEnumerationExecutor executor, int ordinal, LuxembourgDocumentFetchAddress address,
            MachineQueryRendererSource renderer, WireRequestBudget budget, CancellationToken cancellationToken)
        {
            if (document is null)
            {
                var bound = new LuxembourgDocumentFetchPlan(address).Bind(
                    $"urn:uuid:{Guid.NewGuid():D}", $"urn:uuid:{Guid.NewGuid():D}", renderer);
                var attempt = await executor.RunDocumentGetAsync(bound.Request, budget, cancellationToken).ConfigureAwait(false);
                var route = attempt.Evidence;
                Fetches.Add(new DocumentFetch(ordinal, address.ArtifactRef, bound.MachinePlanRef.ResourceId,
                    bound.InputArtifact.ArtifactRef.ResourceId,
                    route is null ? null : new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", CustodyDigest.Of(route.CopyCanonicalBytes())),
                    route?.RunIdentity, route?.Hops[0].LogicalRequestSha256,
                    attempt.Refusal, attempt.DeniedRobotsPath, attempt.Detail));
                return attempt;
            }
            if (_index >= document.Fetches.Length)
                throw new CustodyIntegrityException("Selected document requires a missing retained fetch.");
            var fetch = document.Fetches[_index++];
            if (fetch.Ordinal != ordinal || fetch.Address != address.ArtifactRef)
                throw new CustodyIntegrityException("Retained fetch differs from the selected object or file identity.");
            var original = new LuxembourgDocumentFetchPlan(address).Bind(fetch.PlanId, fetch.InputId, renderer);
            if (fetch.Route is null)
            {
                if (fetch.Run is not null || fetch.RequestSha256 is not null ||
                    fetch.Refusal != LuxembourgDocumentGetAttemptRefusal.RobotsDisallowed ||
                    fetch.DeniedRobotsPath != address.FetchUri.AbsolutePath)
                    throw new CustodyIntegrityException("Successful document phase has an inconsistent unexecuted refusal.");
                var refused = LuxembourgDocumentGetAttemptResult.RobotsRefused(fetch.DeniedRobotsPath);
                if (refused.Detail != fetch.Detail)
                    throw new CustodyIntegrityException("Retained robots refusal detail disagrees with its selected path.");
                return refused;
            }
            if (fetch.Run is null || fetch.RequestSha256 is null || fetch.Refusal is not null ||
                fetch.DeniedRobotsPath is not null || fetch.Detail is not null)
                throw new CustodyIntegrityException("Executed document has inconsistent retained evidence.");
            return await LuxembourgDocumentFetchRouteReader.ReopenAsync(store, fetch.Route, fetch.Run,
                fetch.RequestSha256, address, original.Request, cancellationToken).ConfigureAwait(false);
        }
        internal void RequireEnd()
        {
            if (document is null || _index != document.Fetches.Length)
                throw new CustodyIntegrityException("Document checkpoint contains unconsumed fetches.");
        }
    }
}
