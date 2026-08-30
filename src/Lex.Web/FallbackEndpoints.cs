using System.Globalization;
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

    /// <summary>
    /// Whether the client prefers JSON over HTML, by Accept quality rather than token presence.
    ///
    /// Accept is a quality-ranked list. Treating any mention of text/html as a preference hands a
    /// page to a client that wrote text/html;q=0, which is that client stating plainly it will not
    /// take HTML. On a tie the human surface wins: a browser sending both is asking for a page, and
    /// a reader shown raw JSON is worse off than a script handed HTML it can ignore.
    /// </summary>
    private static bool PrefersJson(string accept)
    {
        double json = -1, html = -1;
        foreach (var part in accept.Split(',',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var segments = part.Split(';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var quality = 1d;
            foreach (var parameter in segments.Skip(1))
                if (parameter.StartsWith("q=", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(parameter[2..], NumberStyles.Float,
                                       CultureInfo.InvariantCulture, out var parsed))
                    quality = parsed;

            if (segments[0].Equals("application/json", StringComparison.OrdinalIgnoreCase))
                json = Math.Max(json, quality);
            else if (segments[0].Equals("text/html", StringComparison.OrdinalIgnoreCase))
                html = Math.Max(html, quality);
        }
        // q=0 is a refusal, not a preference, and a tie is not a preference either.
        return json > 0 && json > html;
    }

    public static IEndpointRouteBuilder MapFallbackRoute(
        this IEndpointRouteBuilder app, WebContext ctx)
    {
        // Explicit catch-all. MapFallback(RequestDelegate) defaults to {*path:nonfile}, which
        // excludes anything that looks like a file, so /no-such.css and /api/no-such.json fell
        // through to the zero-byte 404 this module exists to remove.
        app.MapFallback("{*path}", (HttpContext http) =>
        {
            var path = http.Request.Path.Value ?? "";
            var accept = http.Request.Headers.Accept.ToString();
            // A machine client asked for JSON. Handing it an HTML page would be its own small lie
            // about what happened, so the machine lanes and JSON-preferring clients get a typed body.
            var machine = UnderLane(path, "/api")
                || UnderLane(path, "/mcp")
                || PrefersJson(accept);

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
