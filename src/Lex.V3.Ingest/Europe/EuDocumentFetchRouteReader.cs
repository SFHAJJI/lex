using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Custody;
using Lex.V3.Contracts.Source.Core;
using Lex.V3.Contracts.Source.Europe;
using Lex.V3.Contracts.Source.Http;

namespace Lex.V3.Ingest.Europe;

/// <summary>Reopens an original document route and its transport closure without sending a request.</summary>
/// <remarks>
/// The caller supplies the original logical-request pin and the independently selected address.
/// Original write receipts describe capture, not the current store's retention floor. This reader
/// does not create a new observation or prove a package, body, robots verdict or enumeration.
/// </remarks>
public static class EuDocumentFetchRouteReader
{
    public static async Task<EuDocumentFetchAttemptResult> ReopenAsync(ICustodyStore store,
        SourceArtifactRef retainedRoute, SourceArtifactRef expectedRun, string expectedLogicalRequestSha256,
        EuDocumentFetchAddress expectedAddress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(retainedRoute);
        ArgumentNullException.ThrowIfNull(expectedRun);
        ArgumentNullException.ThrowIfNull(expectedAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedLogicalRequestSha256);
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, retainedRoute.Sha256, cancellationToken).ConfigureAwait(false);
        try
        {
            var route = RoutedHttpEvidence.ParseAndVerify(bytes.Span);
            if (route.RunIdentity != expectedRun || route.Hops.Count == 0 ||
                route.Hops[0].LogicalRequestSha256 != expectedLogicalRequestSha256 ||
                route.Hops[0].RequestUri != expectedAddress.ResourceUri)
                throw new CustodyIntegrityException("Document route differs from its original run, request or address.");
            HttpLogicalRequest? first = null;
            var receipts = new Dictionary<string, DurableBlobWriteReceipt>(StringComparer.Ordinal);
            foreach (var hop in route.Hops)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var requestBytes = await CustodyRestore.ReadByDigestCheckedAsync(store,
                    hop.LogicalRequestSha256, cancellationToken).ConfigureAwait(false);
                var request = HttpLogicalRequest.ParseAndVerify(requestBytes.Span);
                first ??= request;
                if (request.Uri != hop.RequestUri || request.Method != HttpRequestMethod.Get ||
                    request.Body.Length != 0 || request.Body.Sha256 != CustodyDigest.Of([]) ||
                    request.RequestPolicySha256 != first.RequestPolicySha256 ||
                    request.RedirectPolicySha256 != first.RedirectPolicySha256 ||
                    !request.Headers.SequenceEqual(first.Headers) ||
                    request.Headers.Count != 3 || request.Headers[0].Name != "user-agent" ||
                    request.Headers[1] != new HttpLogicalRequestHeader("accept", expectedAddress.Accept) ||
                    request.Headers[2] != new HttpLogicalRequestHeader("accept-language", expectedAddress.AcceptLanguage))
                    throw new CustodyIntegrityException("Document hop disagrees with the original GET representation or policies.");
                _ = await CustodyRestore.ReadByDigestCheckedAsync(store, request.RequestPolicySha256, cancellationToken).ConfigureAwait(false);
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
            return EuDocumentFetchAttemptResult.Executed(reopened, receipts);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException)
        {
            throw new CustodyIntegrityException("Document route failed independent transport verification.", exception);
        }
    }
}
