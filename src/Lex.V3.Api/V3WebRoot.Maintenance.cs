using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Lex.V3.Api;

/// <summary>
/// The release's maintenance revision: the release's own signed image, started with <c>LEX_V3_MAINTENANCE=on</c>, serving
/// the maintenance page and nothing else (the specification of record's R60: "a maintenance page shows during deployments";
/// R108: "a static maintenance page linking Legilux and EUR-Lex" is pre-authorised, done at once and reported; R115: "the
/// fallback for the first public exposure is a static maintenance page"; the plan's item 10.14: an identity that can "switch
/// on a pre-built maintenance revision"). The deploy kit builds it beside the live revision with no traffic, probes it and
/// keeps it inactive (<c>deploy/deploy.ps1 -Maintenance</c>); moving the traffic to it and back is the owner's, as a
/// promotion is.
/// </summary>
/// <remarks>
/// <para>
/// The switch is read once, when the server starts: only the exact value <c>on</c> switches it on, and any other value, its
/// absence included, leaves the server as it is (<see cref="MaintenanceState"/>). On, the server opens no synthetic preview
/// and no corpus mount, so it starts in seconds, copies no index into its temporary folder and serves no law; it opens its
/// web root kept out of search engines, with no indexing record, and answers every request with
/// <see cref="HandleMaintenanceAsync"/> in place of the API's routing (<c>Program.cs</c>).
/// </para>
/// <para>
/// A page request (<see cref="IsPageRequest"/>: a GET or a HEAD whose path is not under <c>/api/</c>, <c>/mcp</c> or
/// <c>/health/</c>) is answered 503 with <c>Retry-After</c> and every header a page carries (<c>X-Robots-Tag: noindex</c> and
/// <c>no-store</c> among them), and the maintenance page as the build wrote it, in the interface language the path stands
/// under, else in the one the reader's languages choose, exactly as the 404 page is chosen (<see cref="NotFoundPageFor"/>,
/// with <c>Vary: Accept-Language</c>); a HEAD gets no body. Readiness (<c>/health/ready</c>) is 204 while the root holds a
/// maintenance page in each interface language and 503 otherwise, so a maintenance revision without its pages never becomes
/// ready and its probe fails. Every other request (the API's routes, the MCP endpoint, any other method) is the transport
/// problem <c>service_in_maintenance</c> (503), with <c>Retry-After</c>. Nothing of a request is read but its method, its raw
/// target and, for a page's language, Accept-Language, by the one read the 404 page makes; nothing is written anywhere.
/// </para>
/// </remarks>
internal sealed partial class V3WebRoot
{
    /// <summary>The release-level switch's variable, read once when the server starts.</summary>
    internal const string MaintenanceVariable = "LEX_V3_MAINTENANCE";

    /// <summary>The switch's one value that makes the server a maintenance revision.</summary>
    internal const string MaintenanceOn = "on";

    /// <summary>How long a client is asked to wait before it asks again, in seconds: <c>Retry-After</c> on every answer in maintenance but readiness.</summary>
    internal const string MaintenanceRetryAfter = "300";

    private static readonly byte[] NoMaintenancePage = "No maintenance page is held in each interface language.\n"u8.ToArray();

    /// <summary>Whether a value of the switch's variable makes the server a maintenance revision: only <c>on</c>, exactly.</summary>
    internal static bool MaintenanceState(string? value) => string.Equals(value, MaintenanceOn, StringComparison.Ordinal);

    /// <summary>The maintenance pages this root holds, as the build names them (<c>maintenance/fr.html</c>).</summary>
    internal IReadOnlyCollection<string> MaintenancePages => (IReadOnlyCollection<string>)_maintenancePages.Keys;

    /// <summary>Whether this root holds a maintenance page in each interface language, which a maintenance revision's readiness asks.</summary>
    internal bool HoldsMaintenancePages => InterfaceLanguages.All(language => _maintenancePages.ContainsKey($"{MaintenanceDirectory}/{language}.html"));

    /// <summary>
    /// Answers a page request with the maintenance page in the reader's language, with status 503, <c>Retry-After</c> and the
    /// headers every page carries, and returns true; returns false, touching nothing, for any other request, or when this root
    /// holds no such page.
    /// </summary>
    internal Task<bool> TryServeMaintenanceAsync(HttpContext context, string rawTarget, CancellationToken cancellationToken) =>
        TryServeBuiltPageAsync(context, rawTarget, MaintenanceDirectory, _maintenancePages, StatusCodes.Status503ServiceUnavailable, MaintenanceRetryAfter, cancellationToken);

    /// <summary>The maintenance revision's request delegate, run in place of the API's (<c>Program.cs</c>), over <paramref name="webRoot"/>, or none.</summary>
    internal static RequestDelegate CreateMaintenanceRequestDelegate(V3WebRoot? webRoot) =>
        context => HandleMaintenanceAsync(webRoot, context, context.RequestAborted);

    /// <summary>
    /// A maintenance revision's answer to a request: its readiness; the maintenance page, for a page request; and the
    /// transport problem <c>service_in_maintenance</c> for every other. With no web root (none, or one refused at startup),
    /// readiness says the revision is not ready and every other request is that problem.
    /// </summary>
    internal static async Task HandleMaintenanceAsync(V3WebRoot? webRoot, HttpContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? string.Empty;
        if (string.Equals(rawTarget, V3ApiHandler.ReadinessRawTarget, StringComparison.Ordinal))
        {
            await WriteMaintenanceReadinessAsync(webRoot, context, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (webRoot is not null && await webRoot.TryServeMaintenanceAsync(context, rawTarget, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // The MCP endpoint names its protocol version on every answer, transport failures included, as when it serves.
        Action<HttpResponse> afterClear = string.Equals(rawTarget, V3ApiHandler.McpRawTarget, StringComparison.Ordinal) ? RetryLaterOverMcp : RetryLater;
        await V3TransportResponse.WriteAsync(context.Response, V3TransportFailureKind.ServiceInMaintenance, afterClear, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// A maintenance revision's readiness: 204 while the root holds a maintenance page in each interface language, and 503 with
    /// a one-line reason when it does not, so such a revision takes no traffic and fails its probe. GET and HEAD only, and
    /// nothing about it is cached, as the readiness of a server that holds the law (<see cref="V3ApiHandler"/>).
    /// </summary>
    private static async Task WriteMaintenanceReadinessAsync(V3WebRoot? webRoot, HttpContext context, CancellationToken cancellationToken)
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

        if (webRoot is not null && webRoot.HoldsMaintenancePages)
        {
            response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        response.ContentType = "text/plain; charset=utf-8";
        response.ContentLength = NoMaintenancePage.Length;
        if (HttpMethods.IsGet(method))
        {
            await response.Body.WriteAsync(NoMaintenancePage, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>What every answer in maintenance but readiness says: when to ask again.</summary>
    private static void RetryLater(HttpResponse response) => response.Headers.RetryAfter = MaintenanceRetryAfter;

    /// <summary><see cref="RetryLater"/>, and the one MCP protocol version served, which every answer of the endpoint names.</summary>
    private static void RetryLaterOverMcp(HttpResponse response)
    {
        RetryLater(response);
        response.Headers["MCP-Protocol-Version"] = V3McpJsonRpc.ProtocolVersion;
    }
}
