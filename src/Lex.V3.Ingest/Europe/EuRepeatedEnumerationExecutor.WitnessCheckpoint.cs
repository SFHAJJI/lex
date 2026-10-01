using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Europe;

public sealed partial class EuRepeatedEnumerationExecutor
{
    private const string WitnessCheckpointSchema = "lex-eu-witness-checkpoint/1";

    /// <summary>Replays the original checked pages through the production traversal, without a publisher session.</summary>
    public static async Task<EuWitnessTraversalResult> RestoreWitnessTraversalAsync(ICustodyStore store,
        SourceArtifactRef checkpoint, IReadOnlyList<EuWatermarkWitnessPlan> expectedPlans,
        SourceArtifactRef expectedRun, SourceArtifactRef expectedRenderer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(expectedPlans);
        ArgumentNullException.ThrowIfNull(expectedRun);
        ArgumentNullException.ThrowIfNull(expectedRenderer);
        var plans = expectedPlans.ToArray();
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, checkpoint.Sha256, cancellationToken).ConfigureAwait(false);
        try
        {
            var document = ContractJson.Deserialize<WitnessCheckpoint>(new UTF8Encoding(false, true).GetString(bytes.Span));
            if (document.Schema != WitnessCheckpointSchema || !bytes.Span.SequenceEqual(EncodeWitnessCheckpoint(document)) ||
                document.Run != expectedRun || document.Renderer != expectedRenderer || document.Plans is null ||
                !document.Plans.SequenceEqual(plans.Select(DescribeWitnessPlan)) || document.Pages is null ||
                document.Pages.Count == 0 || document.ElapsedTicks < 0 || document.ProductRequestCount < document.Pages.Count)
                throw new CustodyIntegrityException("Witness checkpoint framing or caller binding disagrees.");
            if (document.Pages.Any(page => page is null || page.Evidence is null) ||
                document.Pages.Select(page => page.Evidence.HttpEvidenceRef.Sha256).Distinct().Count() != document.Pages.Count ||
                document.Pages.Select(page => page.Evidence.QueryInputRef).Distinct().Count() != document.Pages.Count)
                throw new CustodyIntegrityException("Witness checkpoint repeats or omits an observation.");
            var sourceBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, expectedRenderer.Sha256, cancellationToken).ConfigureAwait(false);
            var renderer = MachineQueryRendererSource.Open(expectedRenderer, sourceBytes.Span);
            var replay = new WitnessReplay(store, checkpoint, document);
            var executor = new EuRepeatedEnumerationExecutor(store, TimeProvider.System);
            var result = await executor.RunWitnessTraversalCoreAsync(plans, renderer, null,
                WireRequestBudget.OfWireRequests(2), cancellationToken, replay).ConfigureAwait(false);
            if (result.Entries is null || result.DeliveryEvidenceSha256 != document.DeliveryEvidenceSha256 ||
                WitnessEntriesDigest(result.Entries) != document.EntriesSha256 || result.ProductRequestCount != 0)
                throw new CustodyIntegrityException("Retained witness did not reproduce its traversal: " + result.Refusal?.Detail);
            return result;
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("Witness checkpoint failed independent verification.", exception);
        }
    }

    private async Task<SourceArtifactRef> RetainWitnessCheckpointAsync(IReadOnlyList<EuWatermarkWitnessPlan> plans,
        SourceArtifactRef renderer, SourceArtifactRef run, IReadOnlyList<WitnessCheckpointPage> pages,
        EuWitnessTraversalResult result, CancellationToken cancellationToken)
    {
        var document = new WitnessCheckpoint(WitnessCheckpointSchema, plans.Select(DescribeWitnessPlan).ToArray(),
            renderer, run, pages, result.DeliveryEvidenceSha256!, WitnessEntriesDigest(result.Entries!),
            result.ProductRequestCount, result.Elapsed.Ticks);
        var bytes = EncodeWitnessCheckpoint(document);
        var (receipt, failure) = await CustodyHold.TryHoldAsync(_custodyStore, bytes, cancellationToken).ConfigureAwait(false);
        if (receipt is null) throw new CustodyRequiredException("Witness checkpoint hold refused: " + failure);
        if (receipt.Reference.ContentSha256 != CustodyDigest.Of(bytes) || receipt.Reference.ByteLength != bytes.Length)
            throw new CustodyIntegrityException("Witness checkpoint receipt names different bytes.");
        return new SourceArtifactRef(NewUrn(), receipt.Reference.ContentSha256);
    }

    private static byte[] EncodeWitnessCheckpoint(WitnessCheckpoint document) => Encoding.UTF8.GetBytes(ContractJson.Serialize(document));
    private static string WitnessEntriesDigest(EuFeedWatermarkEntrySet entries) =>
        CustodyDigest.Of(Encoding.UTF8.GetBytes(ContractJson.Serialize(entries.CanonicalEntries)));
    private sealed record WitnessCheckpoint(string Schema, IReadOnlyList<WitnessPlanCheckpoint> Plans,
        SourceArtifactRef Renderer, SourceArtifactRef Run, IReadOnlyList<WitnessCheckpointPage> Pages,
        string DeliveryEvidenceSha256, string EntriesSha256, int ProductRequestCount, long ElapsedTicks);
    private static WitnessPlanCheckpoint DescribeWitnessPlan(EuWatermarkWitnessPlan plan) =>
        new(plan.ArtifactRef, plan.BatchDigest, plan.StartPosition.WatermarkLexical, plan.StartPosition.CanonicalEntryKey);
    private sealed record WitnessPlanCheckpoint(SourceArtifactRef Profile, string BatchDigest, string StartWatermark, string StartEntryKey);
    private sealed record WitnessCheckpointPage(int BatchOrdinal, RepeatedEnumerationEvidenceRefs Evidence);

    private sealed class WitnessReplay(ICustodyStore store, SourceArtifactRef reference, WitnessCheckpoint document)
    {
        private int _next;
        private readonly HashSet<ulong> _requestOrdinals = [];
        public SourceArtifactRef Reference => reference;
        public TimeSpan HistoricalElapsed => TimeSpan.FromTicks(document.ElapsedTicks);

        public WitnessCheckpointPage Peek(int batchOrdinal)
        {
            if (_next >= document.Pages.Count || document.Pages[_next].BatchOrdinal != batchOrdinal)
                throw new CustodyIntegrityException("Witness checkpoint is missing the next page of the expected batch.");
            return document.Pages[_next];
        }

        public void RequireEnd()
        {
            if (_next != document.Pages.Count)
                throw new CustodyIntegrityException("Witness checkpoint has unconsumed pages after traversal termination.");
        }

        public async Task<ObservationAttemptOutcome> ReadAsync(EuWatermarkWitnessBoundQuery expected,
            int batchOrdinal, CancellationToken cancellationToken)
        {
            var page = Peek(batchOrdinal);
            var refs = page.Evidence;
            var evidence = await new RepeatedEnumerationDeliveryReopenGlue(store).ReopenPageEvidenceAsync(refs, cancellationToken).ConfigureAwait(false);
            var opened = MachineQueryBinder.OpenForSend(expected.Request);
            var body = opened.CopyRequestBody();
            var source = OfficialMachineQuerySourceProfiles.ResolveFor(opened);
            var request = evidence.LogicalRequest;
            var route = evidence.HttpEvidence;
            var terminal = route.Hops[0];
            var headers = new[]
            {
                new HttpLogicalRequestHeader("user-agent", source.CrawlerUserAgent),
                new HttpLogicalRequestHeader("accept", source.Accept!),
                new HttpLogicalRequestHeader("content-type", source.RequestContentType + "; charset=utf-8"),
            };
            if (expected.MachinePlanRef != refs.QueryPlanRef || expected.InputArtifact.ArtifactRef != refs.QueryInputRef ||
                opened.RenderReceiptRef != refs.RenderReceiptRef || expected.Request.RenderReceipt != evidence.RenderReceipt ||
                request.Uri != opened.RequestedUri || request.Method != source.Method || !request.Headers.SequenceEqual(headers) ||
                request.Body.Sha256 != CustodyDigest.Of(body) || request.Body.Length != (ulong)body.Length ||
                route.RunIdentity != document.Run || !_requestOrdinals.Add(route.RequestOrdinal) ||
                route.Outcome is not CompleteHttpRouteOutcome || terminal.LogicalRequestSha256 != refs.LogicalRequestRef.Sha256 ||
                terminal.RequestUri != request.Uri || terminal.Status != 200 || terminal.StatusDisposition != HttpStatusDisposition.DerivableStatus ||
                terminal.Completion is not (DeclaredContentLengthHttpCompletion or PinnedHandlerChunkedEofHttpCompletion) ||
                terminal.Headers.ContentType is not RoutedHttpSingleHeader media || media.Value != EuWatermarkWitnessPlan.ResponseMediaType ||
                evidence.DurableWriteReceipt.Reference.CustodyClass != CustodyClass.NightlyFloor90d ||
                evidence.DurableWriteReceipt.Reference.ContentSha256 != terminal.Sha256 ||
                evidence.DurableWriteReceipt.Reference.ByteLength != evidence.RetainedPayloadBytes.Length ||
                (ulong)evidence.RetainedPayloadBytes.Length != terminal.Length)
                throw new CustodyIntegrityException("Witness page does not match its expected query and original transport.");
            var checkedRoute = RoutedHttpEvidence.Create(route.RunIdentity, route.RequestOrdinal, route.AttemptOrdinal,
                route.Hops, route.Outcome, new Dictionary<string, DurableBlobWriteReceipt>(StringComparer.Ordinal)
                    { [terminal.ObservationId] = evidence.DurableWriteReceipt });
            if (!checkedRoute.CopyCanonicalBytes().AsSpan().SequenceEqual(route.CopyCanonicalBytes()))
                throw new CustodyIntegrityException("Witness receipts do not reproduce the original route.");
            foreach (var digest in new[] { request.RequestPolicySha256, request.RedirectPolicySha256,
                evidence.QueryPlan.RendererProfileRef.Sha256, evidence.QueryPlan.RendererSourceRef.Sha256 })
                _ = await CustodyRestore.ReadByDigestCheckedAsync(store, digest, cancellationToken).ConfigureAwait(false);
            var heldBody = await CustodyRestore.ReadByDigestCheckedAsync(store, request.Body.Sha256, cancellationToken).ConfigureAwait(false);
            if (!heldBody.Span.SequenceEqual(body)) throw new CustodyIntegrityException("Witness request body changed.");
            _next++;
            return new ObservationAttemptOutcome(new RepeatedEnumerationObservedTransport(request, checkedRoute,
                evidence.DurableWriteReceipt, evidence.RetainedPayloadBytes), route.RequestOrdinal, null);
        }
    }
}
