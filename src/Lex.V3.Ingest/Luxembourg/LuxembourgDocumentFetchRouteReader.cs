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
/// This readback does not establish a fresh robots verdict: <see cref="ReopenAsync"/> (a GET sent on a session of its
/// own, which never retained its robots fetch) asserts none, and <see cref="ReopenAdmittedAsync"/> (a GET a document
/// phase's shared session sent) re-derives the original verdict from the retained robots route that evaluated it.
/// Historical write receipts do not establish the current store's retention floor. Lookup
/// uses retainedRoute.Sha256, not its ResourceId. No new observation or body proof is minted.
/// </remarks>
public static class LuxembourgDocumentFetchRouteReader
{
    /// <summary>A GET sent on a session of its own: the run's first and only product request.</summary>
    public static Task<LuxembourgDocumentGetAttemptResult> ReopenAsync(ICustodyStore store,
        SourceArtifactRef retainedRoute, SourceArtifactRef expectedRun, string expectedLogicalRequestSha256,
        LuxembourgDocumentFetchAddress expectedAddress, BoundMachineRequest originalBoundRequest, CancellationToken cancellationToken) =>
        ReopenCoreAsync(store, retainedRoute, expectedRun, expectedLogicalRequestSha256, expectedAddress, originalBoundRequest,
            robotsRoute: null, cancellationToken);

    /// <summary>
    /// A GET a document phase's shared session sent, any product request of that run, and the robots route of the same run
    /// that evaluated its URL: the route must reopen as this profile's robots route and reproduce its bytes, its policy
    /// must allow this document's exact path (Decision 83), and it must have been younger than the profile's maximum
    /// robots age when this GET started.
    /// </summary>
    internal static Task<LuxembourgDocumentGetAttemptResult> ReopenAdmittedAsync(ICustodyStore store,
        SourceArtifactRef retainedRoute, SourceArtifactRef expectedRun, string expectedLogicalRequestSha256,
        LuxembourgDocumentFetchAddress expectedAddress, BoundMachineRequest originalBoundRequest, SourceArtifactRef robotsRoute,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(robotsRoute);
        return ReopenCoreAsync(store, retainedRoute, expectedRun, expectedLogicalRequestSha256, expectedAddress, originalBoundRequest,
            robotsRoute, cancellationToken);
    }

    /// <summary>
    /// A document a shared session's policy refused: the retained robots route must reopen as this profile's robots route,
    /// and its policy must disallow this document's exact path, as it did when the phase ran.
    /// </summary>
    internal static async Task<LuxembourgDocumentGetAttemptResult> ReopenRobotsRefusalAsync(ICustodyStore store,
        SourceArtifactRef robotsRoute, LuxembourgDocumentFetchAddress expectedAddress, BoundMachineRequest originalBoundRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(robotsRoute);
        ArgumentNullException.ThrowIfNull(expectedAddress);
        ArgumentNullException.ThrowIfNull(originalBoundRequest);
        try
        {
            var identity = MachineQueryBinder.OpenIdentity(originalBoundRequest);
            var profile = OfficialMachineQuerySourceProfiles.ResolveFor(identity);
            if (profile.Id != OfficialMachineQuerySourceProfileId.LuxembourgDocumentFetch ||
                identity.RequestedUri != expectedAddress.FetchUri.AbsoluteUri)
                throw new CustodyIntegrityException("Bound request does not name the selected Luxembourg document.");
            var (_, policy) = await ReopenRobotsAsync(store, robotsRoute, profile, cancellationToken).ConfigureAwait(false);
            var path = expectedAddress.FetchUri.PathAndQuery;
            if (RobotsExclusionPolicy.Evaluate(policy.Span, profile.RobotsProductToken, path) != RobotsPolicyEvaluationResult.Denied)
                throw new CustodyIntegrityException("The retained robots policy does not refuse the document it was recorded as refusing.");
            return LuxembourgDocumentGetAttemptResult.RobotsRefused(path, robotsRoute);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException or FormatException)
        {
            throw new CustodyIntegrityException("Robots route failed independent transport verification.", exception);
        }
    }

    private static async Task<LuxembourgDocumentGetAttemptResult> ReopenCoreAsync(ICustodyStore store,
        SourceArtifactRef retainedRoute, SourceArtifactRef expectedRun, string expectedLogicalRequestSha256,
        LuxembourgDocumentFetchAddress expectedAddress, BoundMachineRequest originalBoundRequest, SourceArtifactRef? robotsRoute,
        CancellationToken cancellationToken)
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
            // A session of its own sent one product request; a shared session sent this GET as any of its product requests.
            if (route.RunIdentity != expectedRun || route.Hops.Count != 1 ||
                (robotsRoute is null
                    ? route.RequestOrdinal != profile.FirstProductRequestOrdinal
                    : route.RequestOrdinal < profile.FirstProductRequestOrdinal) ||
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
            if (robotsRoute is not null)
            {
                // The verdict this GET relied on, re-derived from the policy the same run fetched and held.
                var (robots, policy) = await ReopenRobotsAsync(store, robotsRoute, profile, cancellationToken).ConfigureAwait(false);
                if (robots.RunIdentity != route.RunIdentity)
                    throw new CustodyIntegrityException("The robots route belongs to another run than the document it admitted.");
                if (RobotsExclusionPolicy.Evaluate(policy.Span, profile.RobotsProductToken, expectedAddress.FetchUri.PathAndQuery) !=
                    RobotsPolicyEvaluationResult.Allowed)
                    throw new CustodyIntegrityException("The retained robots policy does not allow the document it admitted.");
                if (profile.EvaluateRobotsPolicyFreshness(Instant(robots.Hops[0].RequestStartedAt), Instant(route.Hops[0].RequestStartedAt)) !=
                    RobotsPolicyFreshness.Current)
                    throw new CustodyIntegrityException("The robots policy was older than the profile's maximum age when the document was fetched.");
            }

            return LuxembourgDocumentGetAttemptResult.Executed(reopened, retryable && spent, robotsRoute);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or DecoderFallbackException or FormatException)
        {
            throw new CustodyIntegrityException("Document route failed independent transport verification.", exception);
        }
    }

    /// <summary>
    /// A retained robots route reopened as this profile's robots route: request ordinal zero, the profile's exact steps
    /// (each URL and status), a complete outcome, every hop's receipt and body held, and the route's bytes reproduced from
    /// them. Returns the route and its terminal body, the policy.
    /// </summary>
    private static async Task<(RoutedHttpEvidence Route, ReadOnlyMemory<byte> Policy)> ReopenRobotsAsync(ICustodyStore store,
        SourceArtifactRef robotsRoute, OfficialMachineQuerySourceProfile profile, CancellationToken cancellationToken)
    {
        var bytes = await CustodyRestore.ReadByDigestCheckedAsync(store, robotsRoute.Sha256, cancellationToken).ConfigureAwait(false);
        var route = RoutedHttpEvidence.ParseAndVerify(bytes.Span);
        var steps = profile.RobotsRoute.Steps;
        if (route.RequestOrdinal != profile.RobotsRequestOrdinal || route.Outcome is not CompleteHttpRouteOutcome ||
            route.Hops.Count != steps.Count)
            throw new CustodyIntegrityException("The retained robots route is not this profile's robots route.");
        var receipts = new Dictionary<string, DurableBlobWriteReceipt>(StringComparer.Ordinal);
        ReadOnlyMemory<byte> policy = default;
        for (var index = 0; index < route.Hops.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hop = route.Hops[index];
            if (hop.RequestUri != steps[index].RequestedUri || hop.Status != steps[index].ExpectedStatusCode)
                throw new CustodyIntegrityException("The retained robots route does not follow this profile's robots steps.");
            var receiptBytes = await CustodyRestore.ReadByDigestCheckedAsync(store, hop.DurableWriteReceiptSha256, cancellationToken)
                .ConfigureAwait(false);
            receipts.Add(hop.ObservationId, ContractJson.Deserialize<DurableBlobWriteReceipt>(new UTF8Encoding(false, true).GetString(receiptBytes.Span)));
            var body = await CustodyRestore.ReadByDigestCheckedAsync(store, hop.Sha256, cancellationToken).ConfigureAwait(false);
            if ((ulong)body.Length != hop.Length)
                throw new CustodyIntegrityException("A robots hop body has the wrong retained length.");
            policy = body;
        }

        var reopened = RoutedHttpEvidence.Create(route.RunIdentity, route.RequestOrdinal, route.AttemptOrdinal, route.Hops,
            route.Outcome, receipts);
        if (!bytes.Span.SequenceEqual(reopened.CopyCanonicalBytes()))
            throw new CustodyIntegrityException("Robots receipts do not reproduce the retained robots route.");
        return (reopened, policy);
    }

    private static DateTimeOffset Instant(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static bool HasExactly(string[] lines, string prefix, string value)
    {
        var matches = lines.Where(line => line.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        return matches.Length == 1 && matches[0] == prefix + value;
    }
}
