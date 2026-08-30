using System.Globalization;
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
    // What this route can actually send. Negotiation compares the ranges against these exact
    // representations rather than against a type name, because a range may name media
    // parameters and a representation either carries them or does not.
    private static readonly MediaTypeHeaderValue JsonRepresentation =
        MediaTypeHeaderValue.Parse("application/json");
    private static readonly MediaTypeHeaderValue HtmlRepresentation =
        MediaTypeHeaderValue.Parse("text/html");

    private static bool PrefersJson(string accept)
    {
        if (!MediaTypeHeaderValue.TryParseList([accept], out var ranges) || ranges.Count == 0)
            return false;

        var json = QualityFor(ranges, JsonRepresentation);
        var html = QualityFor(ranges, HtmlRepresentation);
        // q=0 is a refusal, not a preference, and a tie is not a preference either.
        return json > 0 && json > html;
    }

    /// <summary>
    /// The quality the client attached to one representation, taken from the MOST SPECIFIC range
    /// that covers it, or -1 when nothing covers it.
    /// </summary>
    private static double QualityFor(
        IList<MediaTypeHeaderValue> ranges, MediaTypeHeaderValue representation)
    {
        var specificity = -1;
        var quality = -1d;
        foreach (var range in ranges)
        {
            // A range whose q cannot be read states nothing, so it neither wins nor blocks a
            // less specific range that did state something.
            if (QualityOf(range) is not { } stated) continue;

            // Coverage is decided against the representation this route would really send,
            // with only q removed. MatchesMediaType ignores media parameters, so a client
            // asking for application/json;profile="x" was answered with generic JSON that has
            // no such profile: agreeing to a request we cannot honour. IsSubsetOf compares
            // parameters, which is exactly what is wanted once q is gone, and q is the reason
            // it appeared not to work before.
            var offered = Narrowing(range);
            if (!representation.IsSubsetOf(offered)) continue;

            // Type precision first, then whether the range narrowed it further with media
            // parameters, so a parameterised range outranks the bare type it extends.
            var precision = (range.MatchesAllTypes ? 0 : range.MatchesAllSubTypes ? 1 : 2) * 2
                + (offered.Parameters.Count > 0 ? 1 : 0);
            if (precision <= specificity) continue;
            specificity = precision;
            quality = stated;
        }
        return quality;
    }

    /// <summary>
    /// The range reduced to the parameters that actually narrow what is being asked for.
    ///
    /// q is a preference, not part of the representation. charset is a transport detail: these
    /// bodies are UTF-8 whatever the range says, Accept-Charset is obsolete, and a client naming
    /// it is asking for what this route already sends. Declining that was the mirror of the bug
    /// that ignored parameters entirely, and it turned application/json;charset=utf-8, which is the
    /// representation this route emits, into a request it claimed it could not satisfy.
    ///
    /// Every other media parameter does narrow the representation and is kept, so a range naming a
    /// profile or a version this route cannot produce is still declined.
    /// </summary>
    private static MediaTypeHeaderValue Narrowing(MediaTypeHeaderValue range)
    {
        var offered = range.Copy();
        foreach (var transport in (string[])["q", "charset"])
            while (NameValueHeaderValue.Find(offered.Parameters, transport) is { } parameter)
                offered.Parameters.Remove(parameter);
        return offered;
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
    private static double? QualityOf(MediaTypeHeaderValue range)
    {
        var stated = range.Parameters
            .Where(p => p.Name.Equals("q", StringComparison.OrdinalIgnoreCase)).ToList();
        // RFC 7231: a range with no q means q=1. Two q parameters are two answers to one
        // question, which is not an answer, and the framework silently keeps the first.
        if (stated.Count == 0) return 1d;
        if (stated.Count > 1) return null;
        return Qvalue(stated[0].Value.ToString());
    }

    /// <summary>
    /// A qvalue by the RFC 7231 grammar, or null when the text is not one.
    ///
    /// MediaTypeHeaderValue.Quality is more permissive than the grammar: q=1e0 and q=1.0000
    /// both arrive as 1, and a duplicate q silently keeps the first. Reading those as stated
    /// lets a value the client got wrong assert the loudest preference in the header, which is
    /// the same defect as reading an absent q as 1.
    /// </summary>
    private static double? Qvalue(string text)
    {
        // qvalue = ( "0" [ "." 0*3DIGIT ] ) / ( "1" [ "." 0*3("0") ] ). A token, never quoted.
        if (text.Length is 0 or > 5 || text[0] is not ('0' or '1')) return null;
        if (text.Length == 1) return text[0] - '0';
        if (text[1] != '.') return null;
        var fraction = text.AsSpan(2);
        if (fraction.Length > 3) return null;
        foreach (var digit in fraction)
        {
            if (digit is < '0' or > '9') return null;
            // 1.001 is above the maximum, not a preference just under it.
            if (text[0] == '1' && digit != '0') return null;
        }
        return double.Parse(text, CultureInfo.InvariantCulture);
    }

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

            // The body of this URL depends on the request headers, so a shared cache must not
            // hand one client the representation negotiated for another. Without this a proxy
            // could serve the JSON refusal to a browser, or the page to an MCP client.
            http.Response.Headers.Vary = "Accept";

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
