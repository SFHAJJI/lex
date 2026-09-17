using System.Text.Json;
using Lex.V3.Contracts.Platform;
using Microsoft.AspNetCore.Http.Features;

namespace Lex.V3.Api;

internal static class V3ResolveRestRoute
{
    public const string RawTarget = "/api/v3/resolve";

    public static async Task WriteSuccessAsync(
        HttpContext context,
        V3PlatformHost host,
        string requestReference,
        V3EnvelopeContext envelopeContext,
        Func<V3PlatformOperationRequest, V3PlatformOperationResult> execute,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(host);
        RequireClaimedRequest(context);
        var request = await ReadBoundedBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        await host.WriteRestSuccessAsync(
            context.Response,
            request,
            requestReference,
            envelopeContext,
            execute,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteRefusalAsync(
        HttpContext context,
        V3PlatformHost host,
        string requestReference,
        V3EnvelopeContext envelopeContext,
        Func<V3PlatformOperationRequest, V3PlatformOperationRefusal> execute,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(host);
        RequireClaimedRequest(context);
        var request = await ReadBoundedBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        await host.WriteRestRefusalAsync(
            context.Response,
            request,
            requestReference,
            envelopeContext,
            execute,
            cancellationToken).ConfigureAwait(false);
    }

    private static void RequireClaimedRequest(HttpContext context)
    {
        var rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? string.Empty;
        if (!string.Equals(context.Request.Method, HttpMethods.Post, StringComparison.Ordinal) ||
            !string.Equals(rawTarget, RawTarget, StringComparison.Ordinal))
        {
            throw new JsonException("The request does not match the reviewed resolve REST route.");
        }
    }

    private static async Task<byte[]> ReadBoundedBodyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ContentLength is > V3PlatformHost.MaximumRequestBytes)
        {
            throw new JsonException("The operation request exceeds its byte ceiling.");
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
                throw new JsonException("The operation request exceeds its byte ceiling.");
            }

            body.Write(buffer, 0, read);
        }

        return body.ToArray();
    }
}
