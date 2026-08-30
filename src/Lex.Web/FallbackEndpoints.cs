using static Lex.Web.PageShell;

namespace Lex.Web;

/// <summary>
/// The last route: every request that matched no endpoint.
///
/// Before this module existed an unmatched path returned the framework default, HTTP 404 with a
/// zero-byte body. No chrome, no heading, no reason, no way forward. It was the largest refusal
/// surface on the site and it said nothing at all.
///
/// That is a truth defect rather than a missing nicety. An unmounted publisher prefix and a stale
/// work address both land here, so a reader who typed a law's address and met a blank page could
/// reasonably read it as Lex asserting that law does not exist. Absence of a route is not absence
/// of law, and the copy now says so rather than leaving the reader to guess.
/// </summary>
public static class FallbackEndpoints
{
    private const string NoIndexFollow = "<meta name=\"robots\" content=\"noindex,follow\">";

    /// <summary>
    /// Segment-aware lane matching. A prefix test classifies <c>/apiculture</c> and <c>/mcproxy</c>
    /// as machine lanes and hands a reader JSON for an ordinary mistyped page.
    /// </summary>
    private static bool UnderLane(string path, string lane) =>
        string.Equals(path, lane, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(lane + "/", StringComparison.OrdinalIgnoreCase);

    public static IEndpointRouteBuilder MapFallbackRoute(
        this IEndpointRouteBuilder app, WebContext ctx)
    {
        app.MapFallback((HttpContext http) =>
        {
            var path = http.Request.Path.Value ?? "";
            var accept = http.Request.Headers.Accept.ToString();
            // A machine client asked for JSON. Handing it an HTML page would be its own small lie
            // about what happened, so the machine lanes and JSON-preferring clients get a typed body.
            var machine = UnderLane(path, "/api")
                || UnderLane(path, "/mcp")
                || (accept.Contains("application/json", StringComparison.OrdinalIgnoreCase)
                    && !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase));

            if (machine)
                // A local HTTP fallback token, deliberately not a McpStatus: that closed set
                // describes legal results, and this describes a URL that matched no route.
                return Results.Json(new { status = "unknown_route" }, statusCode: 404);

            return Results.Content(
                Page(ctx.PublicBase, "Page not found", TrustNotices.UnknownRoute(),
                     extraHead: NoIndexFollow),
                "text/html", statusCode: 404);
        });
        return app;
    }
}
