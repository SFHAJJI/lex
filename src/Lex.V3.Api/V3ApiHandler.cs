using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lex.V3.Contracts;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http.Features;

namespace Lex.V3.Api;

internal sealed class V3ApiHandler
{
    private const string EmptySha256 =
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private readonly SyntheticApiState? _syntheticState;
    private readonly V3PlatformHost _host;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly V3CorpusMount? _corpusMount;
    private readonly V3WebRoot? _webRoot;
    private readonly V3ClientLimiter _limiter;

    public V3ApiHandler(SyntheticApiState syntheticState, Func<DateTimeOffset> utcNow)
        : this(syntheticState, new V3PlatformHost(), utcNow, null)
    {
    }

    internal V3ApiHandler(
        SyntheticApiState syntheticState,
        V3PlatformHost host,
        Func<DateTimeOffset> utcNow)
        : this(syntheticState, host, utcNow, null)
    {
    }

    /// <param name="syntheticState">The synthetic preview, in a test build or the S0-05 image; null in the one-server image,
    /// which ships no preview (plan 0.7): that server answers its readiness from its mount, and any path nothing claims as
    /// an unknown route.</param>
    /// <param name="limiter">The per-client limit (R109; <see cref="V3ClientLimiter"/>): one of its own when not given.</param>
    internal V3ApiHandler(
        SyntheticApiState? syntheticState,
        V3PlatformHost host,
        Func<DateTimeOffset> utcNow,
        V3CorpusMount? corpusMount,
        V3WebRoot? webRoot = null,
        V3ClientLimiter? limiter = null)
    {
        _syntheticState = syntheticState;
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _corpusMount = corpusMount;
        _webRoot = webRoot;
        _limiter = limiter ?? new V3ClientLimiter();
    }

    internal static RequestDelegate CreateRequestDelegate(
        SyntheticApiState? syntheticState,
        Func<DateTimeOffset> utcNow,
        V3CorpusMount? corpusMount = null,
        V3WebRoot? webRoot = null)
    {
        // The server's own answers name the commit its build stamped (R23, "service running").
        var api = new V3ApiHandler(syntheticState, new V3PlatformHost(V3PlatformHost.StampedServingRuntimeSourceCommit), utcNow, corpusMount, webRoot);
        return context => api.HandleAsync(context, context.RequestAborted);
    }

    private V3EnvelopeContext UnmountedLuxembourgContext() => UnmountedLuxembourgContext(_utcNow);

    /// <summary>
    /// Today in Luxembourg by this server's clock (R13, <see cref="LuxembourgCalendar"/>), as <c>yyyy-MM-dd</c>; null for a
    /// clock outside the range the rule is held for, when a readable address's page is served as built.
    /// </summary>
    private string? LuxembourgToday() => LuxembourgTodayAt(_utcNow());

    /// <summary>
    /// Today in Luxembourg at <paramref name="now"/> (R13, <see cref="LuxembourgCalendar"/>), as <c>yyyy-MM-dd</c>, the date a
    /// readable address with no date of its own is rendered at; null outside the range the rule is held for.
    /// </summary>
    internal static string? LuxembourgTodayAt(DateTimeOffset now) =>
        now >= LuxembourgCalendar.ValidFrom && now < LuxembourgCalendar.ValidUntil
            ? LuxembourgCalendar.DateAt(now).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            : null;

    /// <summary>Shared with <see cref="V3McpJsonRpc"/> so the MCP transport refuses a call with no corpus mounted the same way REST does.</summary>
    internal static V3EnvelopeContext UnmountedLuxembourgContext(Func<DateTimeOffset> utcNow) =>
        new(
            PublisherId.LuLegilux,
            "refusal",
            TimelineSemantics.PublisherApplicability,
            new V3SnapshotReference("no-corpus-mounted", EmptySha256),
            "lu",
            false,
            new V3Freshness(utcNow(), "unreachable"));

    public async Task HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        // Every answer carries the host's security headers; a page replaces the policy with its own (V3HostHeaders).
        V3HostHeaders.Apply(context.Response);
        var rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? string.Empty;
        // Every registered operation has a route (the specification of record, §3: "All 27 registered operations are served, or
        // refuse with the typed reason their capability manifest states"): the served ones answer from the mount, and the ones
        // served from no data refuse inside a normal envelope (V3RestRouteBinding.Refusing).
        var binding = V3RestRouteBinding.Routed.FirstOrDefault(routed => routed.Claims(rawTarget));
        if (binding is not null || string.Equals(rawTarget, McpRawTarget, StringComparison.Ordinal))
        {
            // An operation, over REST or MCP: counted against its client's limit (R109), and holding one of the client's
            // in-flight places until it is answered.
            using var admission = _limiter.AdmitApi(ClientOf(context));
            if (binding is null)
            {
                await HandleMcpAsync(context, admission, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await HandleRouteAsync(binding, context, admission, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        await HandleOtherAsync(context, rawTarget, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The client a request comes from, as the limit knows it (<see cref="V3ClientLimiter.ClientOf"/>): the one place this
    /// server reads the connection's address and the address the platform's ingress forwards, each handed whole to the limit's
    /// keyed hash and kept nowhere (S4-A11; <c>PublicRequestRecordingTests</c> counts this read).
    /// </summary>
    private V3ClientLimiter.Client? ClientOf(HttpContext context) =>
        _limiter.ClientOf(context.Connection.RemoteIpAddress, context.Request.Headers[V3ClientLimiter.ForwardedForHeader].ToString());

    /// <summary>
    /// One page render's place for the request's client (<see cref="V3ClientLimiter.AdmitRender"/>), held until it is disposed,
    /// or null when the client's render budget or in-flight places are spent, and the page is then served as built.
    /// </summary>
    private V3ClientLimiter.Admission? AdmitRender(HttpContext context)
    {
        var admission = _limiter.AdmitRender(ClientOf(context));
        if (admission.Admitted)
        {
            return admission;
        }

        admission.Dispose();
        return null;
    }

    /// <summary>
    /// The outcome of an operation request over its client's limit (R109): the typed refusal <c>rate_limited</c>, with the wait
    /// and <c>retryable</c>, in the mounted corpus's refusal context, or with no corpus mounted the unmounted one; the same
    /// over REST and MCP. Nothing is asked of the mount.
    /// </summary>
    internal static V3PlatformOperationOutcome RateLimitedOutcome(
        V3CorpusMount? corpusMount,
        Func<DateTimeOffset> utcNow,
        V3PlatformOperationRequest request,
        int retryAfterSeconds)
    {
        ArgumentNullException.ThrowIfNull(utcNow);
        return V3PlatformOperationOutcome.Refused(
            corpusMount?.RefusalContext(utcNow()) ?? UnmountedLuxembourgContext(utcNow),
            RateLimited(request, retryAfterSeconds));
    }

    /// <summary>The refusal <c>rate_limited</c>: the seconds to wait, as <c>Retry-After</c> says them, and that it may be retried.</summary>
    internal static V3PlatformOperationRefusal RateLimited(V3PlatformOperationRequest request, int retryAfterSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retryAfterSeconds);
        using var helpful = JsonDocument.Parse(
            "{\"retry_after\":" + retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"retryable\":true}");
        return new V3PlatformOperationRefusal(
            request,
            "rate_limited",
            helpful.RootElement);
    }

    /// <summary>A served REST route's request: answered, or over its client's limit refused <c>rate_limited</c> with 429.</summary>
    private async Task HandleRouteAsync(
        V3RestRouteBinding binding,
        HttpContext context,
        V3ClientLimiter.Admission admission,
        CancellationToken cancellationToken)
    {
        if (!admission.Admitted)
        {
            await V3ResolveRestRoute.HandleRateLimitedAsync(
                    binding,
                    context,
                    _host,
                    RequestReference(context.TraceIdentifier),
                    request => RateLimitedOutcome(_corpusMount, _utcNow, request, admission.RetryAfterSeconds),
                    admission.RetryAfterSeconds,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (_corpusMount is not null)
        {
            await V3ResolveRestRoute.HandleOutcomeAsync(
                    binding,
                    context,
                    _host,
                    RequestReference(context.TraceIdentifier),
                    ExecuteFor(_corpusMount, _utcNow, binding.OperationId),
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await V3ResolveRestRoute.HandleRefusalAsync(
                binding,
                context,
                _host,
                RequestReference(context.TraceIdentifier),
                UnmountedLuxembourgContext(),
                static request => NoCorpusMounted(request),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Every request that is no operation: the API's unknown routes, readiness, the key set, the pages and the preview.</summary>
    private async Task HandleOtherAsync(HttpContext context, string rawTarget, CancellationToken cancellationToken)
    {
        if (rawTarget.StartsWith("/api/v3/", StringComparison.Ordinal))
        {
            // Every registered operation is routed above, so a path here names nothing: a typo, a case variant or a trailing
            // segment, which is the unknown route.
            await V3TransportResponse.WriteAsync(context.Response, V3TransportFailureKind.UnknownRoute, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (_syntheticState is null && string.Equals(rawTarget, ReadinessRawTarget, StringComparison.Ordinal))
        {
            await WriteReadinessAsync(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        // The published key set (R116), from the bytes the repository holds, with or without pages or a corpus.
        if (await V3KeySet.TryServeAsync(context, rawTarget, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // The live pages, when this server holds them: only the files its web root recorded, and a law's
        // address (V3WebRoot.PageForSitePath) served the page that reads it; every other path goes on to the 404 page
        // below, the synthetic routes, or with no preview the unknown route. A readable address asks for today in Luxembourg
        // (R13), by this server's clock. A page rendered from the API's own answer is counted against its client's render
        // budget (R46, V3ClientLimiter), and over it is served as built; no other file is counted.
        if (_webRoot is not null && await _webRoot.TryServeAsync(context, rawTarget, cancellationToken, AnswerFor(context), LuxembourgToday, () => AdmitRender(context)).ConfigureAwait(false))
        {
            return;
        }

        // A page request nothing claims (a GET or HEAD whose path is not a machine's: /api/, /mcp, /health/) is the site's
        // 404 page, in the reader's language (R60; V3WebRoot.NotFound.cs). A machine's path, and any other method, goes on
        // to the preview's routes or the unknown route's problem, as before.
        if (_webRoot is not null && await _webRoot.TryServeNotFoundAsync(context, rawTarget, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (_syntheticState is null)
        {
            await V3TransportResponse.WriteAsync(context.Response, V3TransportFailureKind.UnknownRoute, cancellationToken).ConfigureAwait(false);
            return;
        }

        await SyntheticApiHandler.HandleAsync(context, _syntheticState, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Where the platform's probes and monitoring ask whether this server holds the law (plan 0.7).</summary>
    internal const string ReadinessRawTarget = "/health/ready";

    private static readonly byte[] NoLawMounted = "No law is mounted.\n"u8.ToArray();

    /// <summary>
    /// Whether this server can answer about the law: 204 when a corpus is mounted, and 503 with a one-line reason when none
    /// is, since a server whose mount was refused at startup is up but refuses every question. GET and HEAD only, and
    /// nothing about it is cached.
    /// </summary>
    private async Task WriteReadinessAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var response = context.Response;
        var method = context.Request.Method;
        response.Headers.CacheControl = "no-store";
        response.Headers.XContentTypeOptions = "nosniff";
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
        {
            response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            response.Headers.Allow = "GET, HEAD";
            response.ContentLength = 0;
            return;
        }

        if (_corpusMount is not null)
        {
            response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentLength = NoLawMounted.Length;
        if (HttpMethods.IsGet(method))
        {
            await response.Body.WriteAsync(NoLawMounted, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The MCP streamable HTTP endpoint: one POST, one JSON-RPC message, one JSON answer (or 202 for a notification).</summary>
    internal const string McpRawTarget = "/mcp";

    /// <summary>
    /// The one dispatch from a routed operation id to the mount's method, used by the REST routes and
    /// by every MCP tool call, so the two transports cannot run different code for one operation
    /// (LAUNCH-CONTRACT: REST and MCP derive identical envelopes). Only ids in
    /// <see cref="V3RestRouteBinding.Routed"/> are dispatched (the served ones and the ones served from no data, which refuse);
    /// any other id is a programming error here, never a fall-through to resolve.
    /// </summary>
    internal static Func<V3PlatformOperationRequest, V3PlatformOperationOutcome> ExecuteFor(
        V3CorpusMount corpusMount,
        Func<DateTimeOffset> utcNow,
        string operationId)
    {
        ArgumentNullException.ThrowIfNull(corpusMount);
        ArgumentNullException.ThrowIfNull(utcNow);
        Func<V3PlatformOperationRequest, V3PlatformOperationOutcome> execute = operationId switch
        {
            "resolve" => request => corpusMount.Resolve(request, utcNow()),
            "as_of" => request => corpusMount.AsOf(request, utcNow()),
            "timeline" => request => corpusMount.Timeline(request, utcNow()),
            "article_history" => request => corpusMount.ArticleHistory(request, utcNow()),
            "diff" => request => corpusMount.Diff(request, utcNow()),
            "changes_in_period" => request => corpusMount.ChangesInPeriod(request, utcNow()),
            "in_force_on" => request => corpusMount.InForceOn(request, utcNow()),
            "search" => request => corpusMount.Search(request, utcNow()),
            "coverage" => request => corpusMount.Coverage(request, utcNow()),
            "provenance" => request => corpusMount.Provenance(request, utcNow()),
            "dossier" => request => corpusMount.Dossier(request, utcNow()),
            "citation" => request => corpusMount.Citation(request, utcNow()),
            "cited_by" => request => corpusMount.CitedBy(request, utcNow()),
            "verify" => request => corpusMount.Verify(request, utcNow()),
            "relations" => request => corpusMount.Relations(request, utcNow()),
            "evidence_bundle" => request => corpusMount.EvidenceBundle(request, utcNow()),
            "classification" => request => corpusMount.Classification(request, utcNow()),
            "manifestation" => request => corpusMount.Manifestation(request, utcNow()),
            "status_on" => request => corpusMount.StatusOn(request, utcNow()),
            "browse" => request => corpusMount.Browse(request, utcNow()),
            "ask" => request => corpusMount.Ask(request, utcNow()),
            "events" => request => corpusMount.Events(request, utcNow()),
            "answer_drift" => request => corpusMount.AnswerDrift(request, utcNow()),
            "as_observed" => request => corpusMount.AsObserved(request, utcNow()),
            "concepts" or "knowable_on" or "transposition" => request => corpusMount.Unserved(request, utcNow()),
            _ => throw new ArgumentOutOfRangeException(nameof(operationId), operationId, "Not a routed operation."),
        };

        // An identifier the out-of-scope list names (V3OutOfScope) is refused out_of_corpus_scope by every operation that takes
        // one, before the mount is read, so its official place and who to ask are said the same way everywhere.
        return request => corpusMount.RefuseOutOfScope(request, utcNow()) ?? execute(request);
    }

    /// <summary>
    /// Streamable HTTP, the request half only: a POST carrying one JSON-RPC message answers one JSON
    /// document with the same status semantics as the dispatcher (a JSON-RPC error is still an HTTP
    /// 200), a notification answers 202 with no body, and no server-initiated stream is offered (a
    /// GET is 405). The body ceiling is the platform's; a larger request is 413 before parsing. No
    /// request header is read (S4-A11).
    /// </summary>
    /// <remarks>
    /// Over its client's limit (R109), a request is read and answered as any other, but a tool call answers the typed refusal
    /// <c>rate_limited</c> in a JSON-RPC result, as REST answers it, and the answer carries <c>Retry-After</c>; the status
    /// stays the dispatcher's.
    /// </remarks>
    private async Task HandleMcpAsync(HttpContext context, V3ClientLimiter.Admission admission, CancellationToken cancellationToken)
    {
        // Every answer of the endpoint, transport failures included, names the one protocol version served.
        static void NameProtocolVersion(Microsoft.AspNetCore.Http.HttpResponse response) =>
            response.Headers["MCP-Protocol-Version"] = V3McpJsonRpc.ProtocolVersion;
        NameProtocolVersion(context.Response);
        if (!string.Equals(context.Request.Method, Microsoft.AspNetCore.Http.HttpMethods.Post, StringComparison.Ordinal))
        {
            await V3TransportResponse.WriteAsync(context.Response, V3TransportFailureKind.MethodNotAllowed, NameProtocolVersion, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // The client's own MCP-Protocol-Version header is not read: the API reads nothing about the
        // caller (S4-A11, PublicRequestRecordingTests), so the specification's optional 400 for an
        // unsupported version is not answered; every answer names the one version served instead.
        byte[] body;
        try
        {
            body = await V3ResolveRestRoute.ReadBoundedBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        }
        catch (V3TransportFailureException exception)
        {
            await V3TransportResponse.WriteAsync(context.Response, exception.Kind, NameProtocolVersion, cancellationToken).ConfigureAwait(false);
            return;
        }

        byte[]? answer;
        try
        {
            answer = await V3McpJsonRpc.HandleAsync(
                    body, _host, _corpusMount, _utcNow, RequestReference(context.TraceIdentifier), cancellationToken,
                    admission.Admitted ? null : admission.RetryAfterSeconds)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await V3TransportResponse.WriteAsync(context.Response, V3TransportFailureKind.InternalFailure, NameProtocolVersion, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (!admission.Admitted)
        {
            context.Response.Headers.RetryAfter = admission.RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (answer is null)
        {
            context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status202Accepted;
            return;
        }

        context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength = answer.Length;
        await context.Response.Body.WriteAsync(answer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The answer a page at a law's address is rendered from (R67): what the REST route answers the request the page would
    /// send, byte for byte: the mounted corpus's answer, or with no corpus mounted the refusal every request gets. A request
    /// the route would refuse as malformed is no answer, and the page is then served as built.
    /// </summary>
    private Func<V3SitePage, CancellationToken, byte[]?> AnswerFor(HttpContext context)
    {
        var corpusMount = _corpusMount;
        var requestReference = RequestReference(context.TraceIdentifier);
        return (page, cancellationToken) =>
        {
            try
            {
                return corpusMount is null
                    ? _host.RefuseRest(page.RequestUtf8, requestReference, page.OperationId, UnmountedLuxembourgContext(), static request => NoCorpusMounted(request), cancellationToken)
                    : _host.AnswerRest(page.RequestUtf8, requestReference, page.OperationId, ExecuteFor(corpusMount, _utcNow, page.OperationId), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return null;
            }
        };
    }

    /// <summary>
    /// What the mounted corpus answers <paramref name="operationId"/> with <paramref name="parameters"/>, as the REST route
    /// writes it, asked by the server itself before it serves anything (what the pages tell search engines, R67:
    /// <see cref="V3WebRoot.SearchEngineFacts"/>); null when the route would refuse the request as malformed. No request
    /// carries anything of a caller here.
    /// </summary>
    internal static byte[]? AnswerAtStartup(
        V3CorpusMount corpusMount,
        Func<DateTimeOffset> utcNow,
        string operationId,
        IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(corpusMount);
        // A site page's parameters are strings and a reading's page number (the plan's item 3.7); these are strings.
        var page = new V3SitePage(V3WebRoot.InterfaceLanguages[0], "startup", operationId,
            parameters.ToDictionary(static pair => pair.Key, static pair => (object)pair.Value, StringComparer.Ordinal));
        try
        {
            return new V3PlatformHost(V3PlatformHost.StampedServingRuntimeSourceCommit)
                .AnswerRest(page.RequestUtf8, RequestReference("startup"), operationId, ExecuteFor(corpusMount, utcNow, operationId), CancellationToken.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    internal static V3PlatformOperationRefusal NoCorpusMounted(V3PlatformOperationRequest request)
    {
        using var helpful = JsonDocument.Parse("{\"required_corpus\":\"lu\"}");
        return new V3PlatformOperationRefusal(
            request,
            "no_corpus_mounted",
            helpful.RootElement);
    }

    private static string RequestReference(string traceIdentifier)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(traceIdentifier ?? string.Empty));
        return "http_" + Convert.ToHexStringLower(bytes);
    }
}
