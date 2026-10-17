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
    internal V3ApiHandler(
        SyntheticApiState? syntheticState,
        V3PlatformHost host,
        Func<DateTimeOffset> utcNow,
        V3CorpusMount? corpusMount,
        V3WebRoot? webRoot = null)
    {
        _syntheticState = syntheticState;
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _corpusMount = corpusMount;
        _webRoot = webRoot;
    }

    internal static RequestDelegate CreateRequestDelegate(
        SyntheticApiState? syntheticState,
        Func<DateTimeOffset> utcNow,
        V3CorpusMount? corpusMount = null,
        V3WebRoot? webRoot = null)
    {
        var api = new V3ApiHandler(syntheticState, new V3PlatformHost(), utcNow, corpusMount, webRoot);
        return context => api.HandleAsync(context, context.RequestAborted);
    }

    private V3EnvelopeContext UnmountedLuxembourgContext() => UnmountedLuxembourgContext(_utcNow);

    /// <summary>
    /// Today in Luxembourg by this server's clock (R13, <see cref="LuxembourgCalendar"/>), as <c>yyyy-MM-dd</c>; null for a
    /// clock outside the range the rule is held for, when a readable address's page is served as built.
    /// </summary>
    private string? LuxembourgToday()
    {
        var now = _utcNow();
        return now >= LuxembourgCalendar.ValidFrom && now < LuxembourgCalendar.ValidUntil
            ? LuxembourgCalendar.DateAt(now).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }

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
        var rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? string.Empty;
        var binding = V3RestRouteBinding.Served.FirstOrDefault(served => served.Claims(rawTarget));
        if (binding is not null)
        {
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
            return;
        }

        if (string.Equals(rawTarget, McpRawTarget, StringComparison.Ordinal))
        {
            await HandleMcpAsync(context, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (rawTarget.StartsWith("/api/v3/", StringComparison.Ordinal))
        {
            // A registered operation this mount has no route for is a typed answer of its own
            // (operation_not_served), so a client cannot take it for a path nothing names; the path
            // segment is compared with the reviewed registry's ids exactly, and a query or a trailing
            // segment keeps it a path nothing names, as the served routes' own claim does.
            var operationId = rawTarget["/api/v3/".Length..];
            var registeredAndNotServed =
                V3OperationRegistry.Reviewed.Operations.Any(operation => string.Equals(operation.OperationId, operationId, StringComparison.Ordinal)) &&
                !V3RestRouteBinding.Served.Any(served => string.Equals(served.OperationId, operationId, StringComparison.Ordinal));
            await V3TransportResponse.WriteAsync(
                    context.Response,
                    registeredAndNotServed ? V3TransportFailureKind.OperationNotServed : V3TransportFailureKind.UnknownRoute,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (_syntheticState is null && string.Equals(rawTarget, ReadinessRawTarget, StringComparison.Ordinal))
        {
            await WriteReadinessAsync(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        // The live pages, when this server holds them: only the files its web root recorded, and a law's
        // address (V3WebRoot.PageForSitePath) served the page that reads it; every other path goes on to the 404 page
        // below, the synthetic routes, or with no preview the unknown route. A readable address asks for today in Luxembourg
        // (R13), by this server's clock.
        if (_webRoot is not null && await _webRoot.TryServeAsync(context, rawTarget, cancellationToken, AnswerFor(context), LuxembourgToday).ConfigureAwait(false))
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
    /// The one dispatch from a served operation id to the mount's method, used by the REST routes and
    /// by every MCP tool call, so the two transports cannot run different code for one operation
    /// (LAUNCH-CONTRACT: REST and MCP derive identical envelopes). Only ids in
    /// <see cref="V3RestRouteBinding.Served"/> are dispatched; any other id is a programming error
    /// here, never a fall-through to resolve.
    /// </summary>
    internal static Func<V3PlatformOperationRequest, V3PlatformOperationOutcome> ExecuteFor(
        V3CorpusMount corpusMount,
        Func<DateTimeOffset> utcNow,
        string operationId)
    {
        ArgumentNullException.ThrowIfNull(corpusMount);
        ArgumentNullException.ThrowIfNull(utcNow);
        return operationId switch
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
            _ => throw new ArgumentOutOfRangeException(nameof(operationId), operationId, "Not a served operation."),
        };
    }

    /// <summary>
    /// Streamable HTTP, the request half only: a POST carrying one JSON-RPC message answers one JSON
    /// document with the same status semantics as the dispatcher (a JSON-RPC error is still an HTTP
    /// 200), a notification answers 202 with no body, and no server-initiated stream is offered (a
    /// GET is 405). The body ceiling is the platform's; a larger request is 413 before parsing. No
    /// request header is read (S4-A11).
    /// </summary>
    private async Task HandleMcpAsync(HttpContext context, CancellationToken cancellationToken)
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
                    body, _host, _corpusMount, _utcNow, RequestReference(context.TraceIdentifier), cancellationToken)
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
        var page = new V3SitePage(V3WebRoot.InterfaceLanguages[0], "startup", operationId, parameters);
        try
        {
            return new V3PlatformHost().AnswerRest(page.RequestUtf8, RequestReference("startup"), operationId, ExecuteFor(corpusMount, utcNow, operationId), CancellationToken.None);
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
