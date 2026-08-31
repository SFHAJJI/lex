using Microsoft.AspNetCore.Http.Features;
using Lex.V3.Contracts;

namespace Lex.V3.Api;

internal static class SyntheticApiHandler
{
    public static async Task HandleAsync(
        HttpContext context,
        SyntheticApiState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(state);
        var rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget ?? string.Empty;
        var knownPath = IsKnownPath(rawTarget);
        if (knownPath && !SyntheticRawTarget.IsWithinApplicationBoundary(rawTarget))
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status400BadRequest,
                "urn:lex:v3:preview:invalid-request",
                "Invalid preview request",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!string.Equals(
                context.Request.Method,
                SyntheticResolveRequestContract.V1.Method,
                StringComparison.Ordinal))
        {
            if (knownPath)
            {
                context.Response.Headers.Allow = HttpMethods.Get;
                await WriteProblemAsync(
                    context,
                    StatusCodes.Status405MethodNotAllowed,
                    "urn:lex:v3:preview:method-not-allowed",
                    "Method not allowed",
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await WriteProblemAsync(
                    context,
                    StatusCodes.Status404NotFound,
                    "urn:lex:v3:preview:not-found",
                    "Not found",
                    cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (string.Equals(rawTarget, SyntheticResolveRequestContract.ReadyRawTarget, StringComparison.Ordinal))
        {
            if (state.Ready)
            {
                cancellationToken.ThrowIfCancellationRequested();
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers.XContentTypeOptions = "nosniff";
            }
            else
            {
                await WriteUnavailableAsync(context, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        var request = SyntheticRawTarget.Parse(rawTarget);
        if (!request.Accepted)
        {
            if (knownPath)
            {
                await WriteProblemAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    "urn:lex:v3:preview:invalid-request",
                    "Invalid preview request",
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await WriteProblemAsync(
                    context,
                    StatusCodes.Status404NotFound,
                    "urn:lex:v3:preview:not-found",
                    "Not found",
                    cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (!state.Ready)
        {
            await WriteUnavailableAsync(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var prepared = await state.ResolveAsync(request, cancellationToken).ConfigureAwait(false);
            await BufferedHttpResponse.WritePreparedJsonAsync(
                context.Response,
                StatusCodes.Status200OK,
                "application/json;charset=utf-8",
                prepared.Utf8Json,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            await WriteUnavailableAsync(context, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsKnownPath(string rawTarget) =>
        string.Equals(rawTarget, SyntheticResolveRequestContract.ProductPath, StringComparison.Ordinal) ||
        rawTarget.StartsWith(SyntheticResolveRequestContract.ProductPath + "?", StringComparison.Ordinal) ||
        string.Equals(rawTarget, SyntheticResolveRequestContract.ReadyRawTarget, StringComparison.Ordinal) ||
        rawTarget.StartsWith(SyntheticResolveRequestContract.ReadyRawTarget + "?", StringComparison.Ordinal);

    private static Task WriteUnavailableAsync(HttpContext context, CancellationToken cancellationToken) =>
        WriteProblemAsync(
            context,
            StatusCodes.Status503ServiceUnavailable,
            "urn:lex:v3:preview:unavailable",
            "Preview unavailable",
            cancellationToken);

    private static Task WriteProblemAsync(
        HttpContext context,
        int status,
        string type,
        string title,
        CancellationToken cancellationToken) =>
        BufferedHttpResponse.WriteJsonAsync(
            context.Response,
            status,
            "application/problem+json",
            new PreviewProblem(type, title, status),
            4 * 1024,
            cancellationToken);
}
