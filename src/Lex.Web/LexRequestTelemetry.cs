using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Lex.Web;

internal static class LexRequestTelemetry
{
    public const string ActivitySourceName = "Lex.Web";
    private const string SpanName = "lex.request";
    private static readonly ActivitySource Source = new(ActivitySourceName);
    private static readonly Regex Digest = new(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static async Task ObserveAsync(
        HttpContext context,
        string? digest,
        RequestDelegate next)
    {
        if (Surface(context.Request.Path) is not { } surface)
        {
            await next(context);
            return;
        }

        var activity = Start(context.Request.Headers.TraceParent, out var ambient);
        activity?.SetTag("lex.surface", surface);
        if (digest is not null && Digest.IsMatch(digest))
            activity?.SetTag("lex.digest", digest);
        var failed = false;
        try
        {
            await next(context);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            var status = failed ? StatusCodes.Status500InternalServerError
                : context.Response.StatusCode;
            if (status is < 100 or > 599) status = StatusCodes.Status500InternalServerError;
            activity?.SetTag("http.response.status_code", status);
            activity?.SetTag("lex.response_class", $"{status / 100}xx");
            activity?.Dispose();
            if (ambient is not null) Activity.Current = ambient;
        }
    }

    private static string? Surface(PathString path) => path.Value switch
    {
        "/search" => "search",
        "/mcp" or "/mcp/" => "mcp",
        "/api/ask" => "ask",
        "/api/ask/stream" => "ask_stream",
        _ => null,
    };

    private static Activity? Start(
        Microsoft.Extensions.Primitives.StringValues values,
        out Activity? ambient)
    {
        ambient = null;
        if (values.Count == 1
            && ActivityContext.TryParse(values[0], null, true, out var parent))
            return Source.StartActivity(SpanName, ActivityKind.Server, parent);
        ambient = Activity.Current;
        Activity.Current = null;
        var activity = Source.StartActivity(SpanName, ActivityKind.Server);
        if (activity is null) Activity.Current = ambient;
        return activity;
    }
}

internal sealed class LexRequestTelemetryMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context, IndexRegistry registry) =>
        LexRequestTelemetry.ObserveAsync(context, registry.VerifiedManifestSetId, next);
}

internal static class LexRequestTelemetryExtensions
{
    public static IApplicationBuilder UseLexRequestTelemetry(this IApplicationBuilder app) =>
        app.UseMiddleware<LexRequestTelemetryMiddleware>();
}
