using System.Text.Json;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http.Features;

namespace Lex.V3.Api;

/// <summary>
/// One governed REST route and the single reviewed operation it serves. The host validates every
/// body against the request schema of the bound operation, whose <c>operation_id</c> constant
/// refuses a body naming any other operation as a request-schema failure below the envelope.
/// </summary>
internal sealed record V3RestRouteBinding(string RawTarget, string OperationId)
{
    public static readonly V3RestRouteBinding Resolve = new("/api/v3/resolve", "resolve");
    public static readonly V3RestRouteBinding AsOf = new("/api/v3/as_of", "as_of");
    public static readonly V3RestRouteBinding Timeline = new("/api/v3/timeline", "timeline");
    public static readonly V3RestRouteBinding ArticleHistory = new("/api/v3/article_history", "article_history");
    public static readonly V3RestRouteBinding Diff = new("/api/v3/diff", "diff");
    public static readonly V3RestRouteBinding ChangesInPeriod = new("/api/v3/changes_in_period", "changes_in_period");
    public static readonly V3RestRouteBinding InForceOn = new("/api/v3/in_force_on", "in_force_on");
    public static readonly V3RestRouteBinding Search = new("/api/v3/search", "search");
    public static readonly V3RestRouteBinding Coverage = new("/api/v3/coverage", "coverage");
    public static readonly V3RestRouteBinding Provenance = new("/api/v3/provenance", "provenance");
    public static readonly IReadOnlyList<V3RestRouteBinding> Served = [Resolve, AsOf, Timeline, ArticleHistory, Diff, ChangesInPeriod, InForceOn, Search, Coverage, Provenance];

    public bool Claims(string rawTarget) =>
        string.Equals(rawTarget, RawTarget, StringComparison.Ordinal) ||
        rawTarget.StartsWith(RawTarget + "?", StringComparison.Ordinal);
}

internal static class V3ResolveRestRoute
{
    public const string RawTarget = "/api/v3/resolve";

    public static Task HandleOutcomeAsync(
        HttpContext context,
        V3PlatformHost host,
        string requestReference,
        Func<V3PlatformOperationRequest, V3PlatformOperationOutcome> execute,
        CancellationToken cancellationToken) =>
        HandleOutcomeAsync(V3RestRouteBinding.Resolve, context, host, requestReference, execute, cancellationToken);

    public static async Task HandleOutcomeAsync(
        V3RestRouteBinding binding,
        HttpContext context,
        V3PlatformHost host,
        string requestReference,
        Func<V3PlatformOperationRequest, V3PlatformOperationOutcome> execute,
        CancellationToken cancellationToken)
    {
        try
        {
            await WriteOutcomeAsync(
                binding,
                context,
                host,
                requestReference,
                execute,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (V3TransportFailureException exception)
        {
            await V3TransportResponse.WriteAsync(context.Response, exception.Kind, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await V3TransportResponse.WriteAsync(
                    context.Response,
                    V3TransportFailureKind.InternalFailure,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public static Task HandleSuccessAsync(
        HttpContext context,
        V3PlatformHost host,
        string requestReference,
        V3EnvelopeContext envelopeContext,
        Func<V3PlatformOperationRequest, V3PlatformOperationResult> execute,
        CancellationToken cancellationToken) =>
        HandleSuccessAsync(V3RestRouteBinding.Resolve, context, host, requestReference, envelopeContext, execute, cancellationToken);

    public static async Task HandleSuccessAsync(
        V3RestRouteBinding binding,
        HttpContext context,
        V3PlatformHost host,
        string requestReference,
        V3EnvelopeContext envelopeContext,
        Func<V3PlatformOperationRequest, V3PlatformOperationResult> execute,
        CancellationToken cancellationToken)
    {
        try
        {
            await WriteSuccessAsync(
                binding,
                context,
                host,
                requestReference,
                envelopeContext,
                execute,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (V3TransportFailureException exception)
        {
            await V3TransportResponse.WriteAsync(context.Response, exception.Kind, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await V3TransportResponse.WriteAsync(
                    context.Response,
                    V3TransportFailureKind.InternalFailure,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public static Task HandleRefusalAsync(
        HttpContext context,
        V3PlatformHost host,
        string requestReference,
        V3EnvelopeContext envelopeContext,
        Func<V3PlatformOperationRequest, V3PlatformOperationRefusal> execute,
        CancellationToken cancellationToken) =>
        HandleRefusalAsync(V3RestRouteBinding.Resolve, context, host, requestReference, envelopeContext, execute, cancellationToken);

    public static async Task HandleRefusalAsync(
        V3RestRouteBinding binding,
        HttpContext context,
        V3PlatformHost host,
        string requestReference,
        V3EnvelopeContext envelopeContext,
        Func<V3PlatformOperationRequest, V3PlatformOperationRefusal> execute,
        CancellationToken cancellationToken)
    {
        try
        {
            await WriteRefusalAsync(
                binding,
                context,
                host,
                requestReference,
                envelopeContext,
                execute,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (V3TransportFailureException exception)
        {
            await V3TransportResponse.WriteAsync(context.Response, exception.Kind, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            await V3TransportResponse.WriteAsync(
                    context.Response,
                    V3TransportFailureKind.InternalFailure,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public static Task WriteSuccessAsync(
        HttpContext context,
        V3PlatformHost host,
        string requestReference,
        V3EnvelopeContext envelopeContext,
        Func<V3PlatformOperationRequest, V3PlatformOperationResult> execute,
        CancellationToken cancellationToken) =>
        WriteSuccessAsync(V3RestRouteBinding.Resolve, context, host, requestReference, envelopeContext, execute, cancellationToken);

    public static async Task WriteSuccessAsync(
        V3RestRouteBinding binding,
        HttpContext context,
        V3PlatformHost host,
        string requestReference,
        V3EnvelopeContext envelopeContext,
        Func<V3PlatformOperationRequest, V3PlatformOperationResult> execute,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(host);
        RequireClaimedRequest(context, binding);
        var request = await ReadBoundedBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        await host.WriteRestSuccessAsync(
            context.Response,
            request,
            requestReference,
            binding.OperationId,
            envelopeContext,
            execute,
            cancellationToken).ConfigureAwait(false);
    }

    public static Task WriteOutcomeAsync(
        HttpContext context,
        V3PlatformHost host,
        string requestReference,
        Func<V3PlatformOperationRequest, V3PlatformOperationOutcome> execute,
        CancellationToken cancellationToken) =>
        WriteOutcomeAsync(V3RestRouteBinding.Resolve, context, host, requestReference, execute, cancellationToken);

    public static async Task WriteOutcomeAsync(
        V3RestRouteBinding binding,
        HttpContext context,
        V3PlatformHost host,
        string requestReference,
        Func<V3PlatformOperationRequest, V3PlatformOperationOutcome> execute,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(host);
        RequireClaimedRequest(context, binding);
        var request = await ReadBoundedBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        await host.WriteRestOutcomeAsync(
            context.Response,
            request,
            requestReference,
            binding.OperationId,
            execute,
            cancellationToken).ConfigureAwait(false);
    }

    public static Task WriteRefusalAsync(
        HttpContext context,
        V3PlatformHost host,
        string requestReference,
        V3EnvelopeContext envelopeContext,
        Func<V3PlatformOperationRequest, V3PlatformOperationRefusal> execute,
        CancellationToken cancellationToken) =>
        WriteRefusalAsync(V3RestRouteBinding.Resolve, context, host, requestReference, envelopeContext, execute, cancellationToken);

    public static async Task WriteRefusalAsync(
        V3RestRouteBinding binding,
        HttpContext context,
        V3PlatformHost host,
        string requestReference,
        V3EnvelopeContext envelopeContext,
        Func<V3PlatformOperationRequest, V3PlatformOperationRefusal> execute,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(host);
        RequireClaimedRequest(context, binding);
        var request = await ReadBoundedBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        await host.WriteRestRefusalAsync(
            context.Response,
            request,
            requestReference,
            binding.OperationId,
            envelopeContext,
            execute,
            cancellationToken).ConfigureAwait(false);
    }

    private static void RequireClaimedRequest(HttpContext context, V3RestRouteBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? string.Empty;
        if (!string.Equals(context.Request.Method, HttpMethods.Post, StringComparison.Ordinal) ||
            !string.Equals(rawTarget, binding.RawTarget, StringComparison.Ordinal))
        {
            var kind = string.Equals(rawTarget, binding.RawTarget, StringComparison.Ordinal)
                ? V3TransportFailureKind.MethodNotAllowed
                : V3TransportFailureKind.UnknownRoute;
            throw new V3TransportFailureException(
                kind, $"The request does not match the reviewed {binding.OperationId} REST route.");
        }
    }

    private static async Task<byte[]> ReadBoundedBodyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength is > V3PlatformHost.MaximumRequestBytes)
        {
            throw new V3TransportFailureException(
                V3TransportFailureKind.RequestTooLarge,
                "The operation request exceeds its byte ceiling.");
        }

        using var body = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await request.Body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (body.Length + read > V3PlatformHost.MaximumRequestBytes)
            {
                throw new V3TransportFailureException(
                    V3TransportFailureKind.RequestTooLarge,
                    "The operation request exceeds its byte ceiling.");
            }

            body.Write(buffer, 0, read);
        }

        return body.ToArray();
    }
}
