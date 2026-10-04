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
    // /2: every GET was evaluated by a phase's shared session and names the retained robots route that evaluated it.
    // /1: a session per GET, no robots route retained; still read, with its own rules.
    private const string DocumentCheckpointSchema = "lex-lu-selected-documents-checkpoint/2";
    private const string PriorDocumentCheckpointSchema = "lex-lu-selected-documents-checkpoint/1";

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
        // A journaled run journals each GET under this selection's input digest, and a resumed one replays only the GETs
        // its interrupted run journaled for the same selection.
        using var capture = Progress is null ? new DocumentReplay(null)
            : new DocumentReplay(null, Progress, AcquisitionJournal.LuxembourgDocumentPhase, DocumentInputDigest(manifest, snapshot));
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
    /// <remarks>
    /// Current body holds still run. Captured robots refusals stay refusals, and no fresh robots verdict is fetched: a /2
    /// checkpoint re-derives each GET's original verdict from the retained robots route that evaluated it, and a /1
    /// checkpoint, whose robots fetches were not retained, asserts none.
    /// </remarks>
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
            if (document.Schema is not (DocumentCheckpointSchema or PriorDocumentCheckpointSchema) ||
                !bytes.Span.SequenceEqual(EncodeDocument(document)) ||
                document.InputSha256 != DocumentInputDigest(manifest, snapshot) || document.Renderer != renderer.Reference ||
                document.Fetches is null || document.Fetches.Any(static fetch => fetch is null || fetch.Ordinal < 0))
                throw new CustodyIntegrityException("Document checkpoint framing or selected inputs disagree.");
            using var replay = new DocumentReplay(document);
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
    /// <param name="Robots">
    /// The retained robots route of the shared session that evaluated this GET's URL (a /2 checkpoint, every fetch);
    /// absent from a /1 fetch's bytes, which are therefore unchanged.
    /// </param>
    private sealed record DocumentFetch(int Ordinal, SourceArtifactRef Address, string PlanId, string InputId,
        SourceArtifactRef? Route, SourceArtifactRef? Run, string? RequestSha256,
        LuxembourgDocumentGetAttemptRefusal? Refusal, string? DeniedRobotsPath, string? Detail,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        SourceArtifactRef? Robots = null);

    /// <summary>
    /// One phase's GETs: captured live (no checkpoint), or replayed in order from a retained checkpoint. A live phase of
    /// a journaled run also journals each executed GET under <paramref name="phase"/> and the selection's
    /// <paramref name="inputSha256"/>, and, resuming, first takes the GET its interrupted run journaled for the same
    /// selection, row and address, admitted through the retained-route reader a full replay uses.
    /// </summary>
    private sealed class DocumentReplay(DocumentCheckpoint? document, LuxembourgAcquisitionProgress? progress = null,
        string phase = "", string inputSha256 = "") : IDisposable
    {
        private readonly HashSet<string> _journaled = new(StringComparer.Ordinal);
        private LuxembourgRepeatedEnumerationExecutor.DocumentGetBatch? _batch;
        private int _index;

        public void Dispose() => _batch?.Dispose();

        // An executed fetch reopened through the reader its record calls for: a shared session's GET with the robots
        // route that evaluated it, a GET on a session of its own as before.
        private static Task<LuxembourgDocumentGetAttemptResult> ReopenExecutedAsync(ICustodyStore store, DocumentFetch fetch,
            LuxembourgDocumentFetchAddress address, BoundMachineRequest request, CancellationToken cancellationToken) =>
            fetch.Robots is { } robots
                ? LuxembourgDocumentFetchRouteReader.ReopenAdmittedAsync(store, fetch.Route!, fetch.Run!, fetch.RequestSha256!,
                    address, request, robots, cancellationToken)
                : LuxembourgDocumentFetchRouteReader.ReopenAsync(store, fetch.Route!, fetch.Run!, fetch.RequestSha256!,
                    address, request, cancellationToken);
        internal List<DocumentFetch> Fetches { get; } = [];
        internal async Task<LuxembourgDocumentGetAttemptResult> FetchAsync(ICustodyStore store,
            LuxembourgRepeatedEnumerationExecutor executor, int ordinal, LuxembourgDocumentFetchAddress address,
            MachineQueryRendererSource renderer, WireRequestBudget budget, CancellationToken cancellationToken)
        {
            if (document is null && TakeJournaled(ordinal, address) is { } journaled)
            {
                try
                {
                    var journaledRequest = new LuxembourgDocumentFetchPlan(address).Bind(journaled.PlanId, journaled.InputId, renderer);
                    var reopened = await ReopenExecutedAsync(store, journaled, address, journaledRequest.Request, cancellationToken)
                        .ConfigureAwait(false);
                    Fetches.Add(journaled);
                    progress?.Tally(phase, replayed: true);
                    await JournalAsync(journaled).ConfigureAwait(false);
                    return reopened;
                }
                catch (Exception exception) when (exception is ArgumentException or CustodyRequiredException
                    or CustodyIntegrityException)
                {
                    // A journaled GET that no longer reopens is fetched again: the journal only says where to look.
                }
            }

            if (document is null)
            {
                var bound = new LuxembourgDocumentFetchPlan(address).Bind(
                    $"urn:uuid:{Guid.NewGuid():D}", $"urn:uuid:{Guid.NewGuid():D}", renderer);
                var attempt = await (_batch ??= executor.OpenDocumentGetBatch()).RunAsync(bound.Request, budget, cancellationToken)
                    .ConfigureAwait(false);
                var route = attempt.Evidence;
                var captured = new DocumentFetch(ordinal, address.ArtifactRef, bound.MachinePlanRef.ResourceId,
                    bound.InputArtifact.ArtifactRef.ResourceId,
                    route is null ? null : new SourceArtifactRef($"urn:uuid:{Guid.NewGuid():D}", CustodyDigest.Of(route.CopyCanonicalBytes())),
                    route?.RunIdentity, route?.Hops[0].LogicalRequestSha256,
                    attempt.Refusal, attempt.DeniedRobotsPath, attempt.Detail, attempt.RobotsRoute);
                Fetches.Add(captured);
                progress?.Tally(phase, replayed: false);
                await JournalAsync(captured).ConfigureAwait(false);
                return attempt;
            }
            if (_index >= document.Fetches.Length)
                throw new CustodyIntegrityException("Selected document requires a missing retained fetch.");
            var fetch = document.Fetches[_index++];
            if (fetch.Ordinal != ordinal || fetch.Address != address.ArtifactRef)
                throw new CustodyIntegrityException("Retained fetch differs from the selected object or file identity.");
            var original = new LuxembourgDocumentFetchPlan(address).Bind(fetch.PlanId, fetch.InputId, renderer);
            // A /2 checkpoint names the robots route behind every fetch; a /1 checkpoint names none.
            if ((document.Schema == DocumentCheckpointSchema) != (fetch.Robots is not null))
                throw new CustodyIntegrityException("Retained fetch does not carry the robots evidence its checkpoint version requires.");
            if (fetch.Route is null)
            {
                if (fetch.Run is not null || fetch.RequestSha256 is not null ||
                    fetch.Refusal != LuxembourgDocumentGetAttemptRefusal.RobotsDisallowed ||
                    fetch.DeniedRobotsPath != address.FetchUri.PathAndQuery)
                    throw new CustodyIntegrityException("Successful document phase has an inconsistent unexecuted refusal.");
                var refused = fetch.Robots is { } robots
                    ? await LuxembourgDocumentFetchRouteReader.ReopenRobotsRefusalAsync(store, robots, address, original.Request, cancellationToken)
                        .ConfigureAwait(false)
                    : LuxembourgDocumentGetAttemptResult.RobotsRefused(fetch.DeniedRobotsPath);
                if (refused.Detail != fetch.Detail || refused.DeniedRobotsPath != fetch.DeniedRobotsPath)
                    throw new CustodyIntegrityException("Retained robots refusal detail disagrees with its selected path.");
                return refused;
            }
            if (fetch.Run is null || fetch.RequestSha256 is null || fetch.Refusal is not null ||
                fetch.DeniedRobotsPath is not null || fetch.Detail is not null)
                throw new CustodyIntegrityException("Executed document has inconsistent retained evidence.");
            return await ReopenExecutedAsync(store, fetch, address, original.Request, cancellationToken).ConfigureAwait(false);
        }
        internal void RequireEnd()
        {
            if (document is null || _index != document.Fetches.Length)
                throw new CustodyIntegrityException("Document checkpoint contains unconsumed fetches.");
        }

        /// <summary>
        /// The executed GET the interrupted run journaled for this selection, row and address, taken once. Null when there
        /// is none, or when the journaled record is not one an executed GET leaves: that GET is fetched again.
        /// </summary>
        private DocumentFetch? TakeJournaled(int ordinal, LuxembourgDocumentFetchAddress address)
        {
            if (progress?.Resume is not { } resume ||
                !resume.TryTake(phase, FetchKey(ordinal, address.ArtifactRef), out var payload))
                return null;
            try
            {
                var journaled = ContractJson.Deserialize<JournaledFetch>(payload.GetRawText());
                var fetch = journaled.Fetch;
                return journaled.InputSha256 == inputSha256 && fetch.Ordinal == ordinal && fetch.Address == address.ArtifactRef &&
                    fetch.Route is not null && fetch.Run is not null && fetch.RequestSha256 is not null &&
                    fetch.Refusal is null && fetch.DeniedRobotsPath is null && fetch.Detail is null
                    ? fetch
                    : null;
            }
            catch (Exception exception) when (exception is ArgumentException or JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Journals an executed GET once its route is held (the executor holds it before it returns). A robots refusal or
        /// a GET that did not execute is not journaled: a resume asks again in its own window.
        /// </summary>
        private async Task JournalAsync(DocumentFetch fetch)
        {
            if (progress?.Journal is not { } journal || fetch.Route is null) return;
            var key = FetchKey(fetch.Ordinal, fetch.Address);
            if (!_journaled.Add(key)) return;
            await journal.AppendAsync(phase, key, AcquisitionJournal.Payload(new JournaledFetch(inputSha256, fetch)),
                fetch.Robots is { } robots ? [fetch.Route.Sha256, robots.Sha256] : [fetch.Route.Sha256]).ConfigureAwait(false);
        }
    }
}
