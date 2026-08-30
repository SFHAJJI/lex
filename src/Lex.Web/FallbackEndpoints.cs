using Microsoft.Net.Http.Headers;
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
    /// Whether the client prefers JSON over HTML, by Accept negotiation.
    ///
    /// The framework's strict parser does the parsing. A hand-rolled one got this wrong in four
    /// ways at once: `;;;` threw and returned 500, wildcards were ignored so `text/html;q=0,*/*;q=1`
    /// chose HTML for a client that had just refused it, `application/*` was not recognised as
    /// covering JSON, and a comma inside a quoted parameter split a single range into two.
    ///
    /// Ranking is by specificity first, then quality, which is what RFC 7231 requires: a precise
    /// range beats a wildcard even when the wildcard carries a higher q. On a tie the human surface
    /// wins, because a browser sending both is asking for a page.
    /// </summary>
    private static bool PrefersJson(string accept)
    {
        if (!MediaTypeHeaderValue.TryParseList([accept], out var ranges) || ranges.Count == 0)
            return false;

        var json = QualityFor(ranges, "application/json");
        var html = QualityFor(ranges, "text/html");
        // q=0 is a refusal, not a preference, and a tie is not a preference either.
        return json > 0 && json > html;
    }

    /// <summary>
    /// The quality the client attached to one representation, taken from the MOST SPECIFIC range
    /// that covers it, or -1 when nothing covers it.
    /// </summary>
    private static double QualityFor(IList<MediaTypeHeaderValue> ranges, string representation)
    {
        // The framework owns both hard parts: tokenising quoted parameters and q, and deciding
        // whether a range covers a representation. IsSubsetOf is the one that does NOT work
        // here, because it compares parameters, so application/json is not a subset of
        // */*;q=1 and a client accepting everything looked like one accepting nothing.
        var specificity = -1;
        var quality = -1d;
        foreach (var range in ranges)
        {
            if (!range.MatchesMediaType(representation)) continue;
            // A range whose q cannot be read states nothing, so it neither wins nor blocks a
            // less specific range that did state something.
            if (QualityOf(range) is not { } stated) continue;
            var precision = range.MatchesAllTypes ? 0 : range.MatchesAllSubTypes ? 1 : 2;
            if (precision <= specificity) continue;
            specificity = precision;
            quality = stated;
        }
        return quality;
    }

    /// <summary>
    /// The quality the client stated, or null when it stated one this parser cannot read.
    ///
    /// Quality is null for two different headers: a range carrying no q parameter, which
    /// RFC 7231 defines as q=1, and a range whose q is malformed or out of range, such as
    /// q=.5 or q=1.5. Reading both as 1 lets a value the client got wrong assert the loudest
    /// preference available. That is how application/*;q=1,text/html;q=.5 became a tie and
    /// served a page to a client that had asked for JSON.
    /// </summary>
    private static double? QualityOf(MediaTypeHeaderValue range) =>
        range.Quality is { } q
            ? q is >= 0 and <= 1 ? q : null
            : NameValueHeaderValue.Find(range.Parameters, "q") is null ? 1d : null;

    public static IEndpointRouteBuilder MapFallbackRoute(
        this IEndpointRouteBuilder app, WebContext ctx)
    {
        // Two registrations on purpose, and the split is load-bearing.
        //
        // The machine lanes need a catch-all that admits dots, because /api/no-such.json is an
        // ordinary request there and the default {*path:nonfile} pattern excludes it.
        //
        // The human lane keeps nonfile. A blanket {*path} also swallows requests for static
        // assets, which is exactly why that constraint is the framework default: replacing it
        // turned three passing asset tests from OK into NotFound. An extensionless path is what
        // a reader types; a .css or .js request is the asset lane and an HTML page is not a
        // useful answer to it.
        app.MapFallback("/api/{*rest}", Answer);
        app.MapFallback("/mcp/{*rest}", Answer);
        app.MapFallback(Answer);
        return app;

        IResult Answer(HttpContext http)
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
        }
    }
}
