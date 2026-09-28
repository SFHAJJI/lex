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

    private readonly SyntheticApiState _syntheticState;
    private readonly V3PlatformHost _host;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly V3CorpusMount? _corpusMount;

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

    internal V3ApiHandler(
        SyntheticApiState syntheticState,
        V3PlatformHost host,
        Func<DateTimeOffset> utcNow,
        V3CorpusMount? corpusMount)
    {
        _syntheticState = syntheticState ?? throw new ArgumentNullException(nameof(syntheticState));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _corpusMount = corpusMount;
    }

    internal static RequestDelegate CreateRequestDelegate(
        SyntheticApiState syntheticState,
        Func<DateTimeOffset> utcNow,
        V3CorpusMount? corpusMount = null)
    {
        var api = new V3ApiHandler(syntheticState, new V3PlatformHost(), utcNow, corpusMount);
        return context => api.HandleAsync(context, context.RequestAborted);
    }

    private V3EnvelopeContext UnmountedLuxembourgContext() => UnmountedLuxembourgContext(_utcNow);

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
            await V3TransportResponse.WriteAsync(
                    context.Response,
                    V3TransportFailureKind.UnknownRoute,
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await SyntheticApiHandler.HandleAsync(context, _syntheticState, cancellationToken).ConfigureAwait(false);
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
            _ => throw new ArgumentOutOfRangeException(nameof(operationId), operationId, "Not a served operation."),
        };
    }

    /// <summary>
    /// Streamable HTTP, the request half only: a POST carrying one JSON-RPC message answers one JSON
    /// document with the same status semantics as the dispatcher (a JSON-RPC error is still an HTTP
    /// 200), a notification answers 202 with no body, and no server-initiated stream is offered (a
    /// GET is 405). The body ceiling is the platform's; a larger request is 413 before parsing.
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

        // A client that names a protocol version names the one this server speaks; an absent header
        // is the initialization phase or an older client and is served (the specification's rule).
        if (context.Request.Headers.TryGetValue("MCP-Protocol-Version", out var requestedVersions)
            && requestedVersions.Count > 0
            && !(requestedVersions.Count == 1 && string.Equals(requestedVersions[0], V3McpJsonRpc.ProtocolVersion, StringComparison.Ordinal)))
        {
            await V3TransportResponse.WriteAsync(context.Response, V3TransportFailureKind.UnsupportedProtocolVersion, NameProtocolVersion, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

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
