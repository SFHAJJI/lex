using System.Globalization;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Luxembourg;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Luxembourg;

/// <summary>Reopens a retained Luxembourg document attempt without publisher traffic.</summary>
/// <remarks>
/// The caller supplies the original run/request pins and independently selected address.
/// Only the producer's retained final attempt is restored. Unretained bootstrap and budget
/// refusals cannot be reconstructed here. The original attempt ordinal and pinned policy's
/// retry limit reproduce RetryAllowanceSpent; preceding attempts are not re-proved.
/// Policy bytes are checked by digest. The profile and retry-limit fields are also checked
/// against the supported LU profile. Plan, parameter-set and renderer-source fields must match
/// the original bound request; this is not a complete request-policy parser.
/// This readback does not establish a fresh robots verdict.
/// Historical write receipts do not establish the current store's retention floor. Lookup
/// uses retainedRoute.Sha256, not its ResourceId. No new observation or body proof is minted.
/// </remarks>
public static class LuxembourgDocumentFetchRouteReader
{
    public static async Task<LuxembourgDocumentGetAttemptResult> ReopenAsync(ICustodyStore store,
        SourceArtifactRef retainedRoute, SourceArtifactRef expectedRun, string expectedLogicalRequestSha256,
        LuxembourgDocumentFetchAddress expectedAddress, BoundMachineRequest originalBoundRequest, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(retainedRoute);
        ArgumentNullException.ThrowIfNull(expectedRun);
        ArgumentNullException.ThrowIfNull(expectedAddress);
        ArgumentNullException.ThrowIfNull(originalBoundRequest);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedLogicalRequestSha256);
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, retainedRoute.Sha256, cancellationToken).ConfigureAwait(false);
        try
        {
            var route = RoutedHttpEvidence.ParseAndVerify(bytes.Span);
            var identity = MachineQueryBinder.OpenIdentity(originalBoundRequest);
            var profile = OfficialMachineQuerySourceProfiles.ResolveFor(identity);
            if (profile.Id != OfficialMachineQuerySourceProfileId.LuxembourgDocumentFetch ||
                identity.RequestedUri != expectedAddress.FetchUri.AbsoluteUri ||
                identity.RenderReceipt.RendererProfileRef != expectedAddress.ArtifactRef)
                throw new CustodyIntegrityException("Bound request does not name the selected Luxembourg document.");
            if (route.RunIdentity != expectedRun || route.Hops.Count != 1 ||
                route.RequestOrdinal != profile.FirstProductRequestOrdinal ||
                route.AttemptOrdinal >= (ulong)profile.MaximumAttempts ||
                route.Hops[0].LogicalRequestSha256 != expectedLogicalRequestSha256 ||
                route.Hops[0].RequestUri != expectedAddress.FetchUri.AbsoluteUri)
                throw new CustodyIntegrityException("Document route differs from its original run, request or address.");
            var receipts = new Dictionary<string, DurableBlobWriteReceipt>(StringComparer.Ordinal);
            foreach (var hop in route.Hops)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var requestBytes = await CustodyRestore.ReadByDigestCheckedAsync(store,
                    hop.LogicalRequestSha256, cancellationToken).ConfigureAwait(false);
                var request = HttpLogicalRequest.ParseAndVerify(requestBytes.Span);
                if (request.Uri != hop.RequestUri || request.Method != HttpRequestMethod.Get ||
                    request.Body.Length != 0 || request.Body.Sha256 != CustodyDigest.Of([]) ||
                    request.Headers.Count != 1 ||
                    request.Headers[0] != new HttpLogicalRequestHeader("user-agent", profile.CrawlerUserAgent))
                    throw new CustodyIntegrityException("Document hop disagrees with the original GET representation or policies.");
                var policyBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, request.RequestPolicySha256, cancellationToken).ConfigureAwait(false);
                var policy = new UTF8Encoding(false, true).GetString(policyBytes.Span).Split('\n');
                if (policy.Length < 2 || policy[0] != "lex-http-request-policy/1" || policy[1] != "machine_query_get" ||
                    !HasExactly(policy, "source_profile=", $"{profile.ArtifactRef.ResourceId}\t{profile.ArtifactRef.Sha256}") ||
                    !HasExactly(policy, "query_plan=", $"{identity.RenderReceipt.QueryPlanRef.ResourceId}\t{identity.RenderReceipt.QueryPlanRef.Sha256}") ||
                    !HasExactly(policy, "ordered_parameter_set=", $"{identity.RenderReceipt.OrderedParameterSetRef.ResourceId}\t{identity.RenderReceipt.OrderedParameterSetRef.Sha256}") ||
                    !HasExactly(policy, "renderer_source=", $"{identity.RenderReceipt.RendererSourceRef.ResourceId}\t{identity.RenderReceipt.RendererSourceRef.Sha256}") ||
                    !HasExactly(policy, "renderer_profile=", $"{expectedAddress.ArtifactRef.ResourceId}\t{expectedAddress.ArtifactRef.Sha256}") ||
                    !HasExactly(policy, "maximum_attempts=", profile.MaximumAttempts.ToString(CultureInfo.InvariantCulture)))
                    throw new CustodyIntegrityException("Document policy does not bind the supported Luxembourg profile and retry limit.");
                var addressBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, expectedAddress.ArtifactRef.Sha256, cancellationToken).ConfigureAwait(false);
                if (!addressBytes.Span.SequenceEqual(expectedAddress.CopyCanonicalIdentityBytes()))
                    throw new CustodyIntegrityException("Document address differs from its retained identity.");
                _ = await CustodyRestore.ReadByDigestCheckedAsync(store, request.RedirectPolicySha256, cancellationToken).ConfigureAwait(false);
                var receiptBytes = await CustodyRestore.ReadByDigestCheckedAsync(store,
                    hop.DurableWriteReceiptSha256, cancellationToken).ConfigureAwait(false);
                var receipt = ContractJson.Deserialize<DurableBlobWriteReceipt>(new UTF8Encoding(false, true).GetString(receiptBytes.Span));
                var body = await CustodyRestore.ReadByDigestCheckedAsync(store, hop.Sha256, cancellationToken).ConfigureAwait(false);
                if ((ulong)body.Length != hop.Length)
                    throw new CustodyIntegrityException("Document hop body has the wrong retained length.");
                receipts.Add(hop.ObservationId, receipt);
            }
            // The existing gate validates receipt identities, redirects, completions and route outcomes.
            var reopened = RoutedHttpEvidence.Create(route.RunIdentity, route.RequestOrdinal,
                route.AttemptOrdinal, route.Hops, route.Outcome, receipts);
            if (!bytes.Span.SequenceEqual(reopened.CopyCanonicalBytes()))
                throw new CustodyIntegrityException("Document receipts do not reproduce the original route bytes.");
            cancellationToken.ThrowIfCancellationRequested();
            var retryable = reopened.Hops[0].Status is 408 or 429 or 500 or 502 or 503 or 504;
            var spent = reopened.AttemptOrdinal == (ulong)profile.MaximumAttempts - 1;
            if (retryable && !spent)
                throw new CustodyIntegrityException("A retryable intermediate attempt is not a retained final document result.");
            return LuxembourgDocumentGetAttemptResult.Executed(reopened, retryable && spent);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("Document route failed independent transport verification.", exception);
        }
    }

    private static bool HasExactly(string[] lines, string prefix, string value)
    {
        var matches = lines.Where(line => line.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        return matches.Length == 1 && matches[0] == prefix + value;
    }
}
