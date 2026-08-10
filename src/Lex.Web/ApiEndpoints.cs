using System.Text;
using System.Text.Json.Nodes;
using Lex.Index;
using static Lex.Web.PageShell;
using static Lex.Web.Fragments;

namespace Lex.Web;

/// <summary>
/// The machine-facing surface: the public MCP endpoint, the assistant's two POST routes, and the two flat files a crawler and a load balancer ask for. Nothing here renders a page.
/// </summary>
public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapApi(this IEndpointRouteBuilder app, WebContext ctx)
    {
        var readers = ctx.Registry.All;
        var askService = ctx.Ask;
        string Page(string title, string body, string? subtitle = null, string nav = "",
                    string? h1 = null, string? canonicalPath = null, string? jsonLd = null,
                    string? description = null, string? lang = null)
            => PageShell.Page(ctx.PublicBase, title, body, subtitle, nav, h1, canonicalPath,
                              jsonLd, description, lang);

        // A crawler that is allowed everywhere still has to FIND everything. Without the
        // sitemap line it had to walk /browse fifty works at a time across twenty-nine pages.
        app.MapGet("/robots.txt", () => Results.Text(
            $"User-agent: *\nAllow: /\nSitemap: {ctx.PublicBase}/sitemap.xml\n"));

        // Every work, plus the pages worth indexing. Version URLs are emitted separately below;
        // counts always come from the mounted readers rather than a comment that goes stale.
        app.MapGet("/sitemap.xml", () =>
        {
            var sb = new StringBuilder();
            sb.Append("""<?xml version="1.0" encoding="UTF-8"?>""");
            sb.Append("""<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">""");

            void Url(string path, string priority, string freq)
                => sb.Append($"<url><loc>{ctx.PublicBase}{path}</loc>"
                             + $"<changefreq>{freq}</changefreq><priority>{priority}</priority></url>");

            Url("/", "1.0", "daily");
            foreach (var p in new[] { "/browse", "/coverage", "/decisions", "/built", "/about",
                                      "/how-it-works", "/developers", "/ai", "/verify",
                                      "/architecture", "/architecture/next", "/benchmarks",
                                      "/stories", "/find", "/changed" })
                Url(p, "0.8", "weekly");

            // lastmod is when the PAGE last changed, so it can never be in the future.
            //
            // This first shipped using the work's latest valid_from, which is a different thing
            // entirely: valid_from is when a law takes effect, and 23 works in the corpus are
            // already published with a commencement date years out, one of them in 2030. Search
            // Console rejected every one of them as an invalid date.
            //
            // observed_from is the honest field: when a record for this work entered the corpus.
            // It also carries real signal, because it distinguishes the works the last nightly
            // run actually touched from the ones that have not moved since the first ingest.
            // A row without it simply omits the tag, which is allowed, rather than guessing.
            var today = ctx.Today;
            string Lastmod(string? observed) =>
                observed is { Length: >= 10 } o && DateOnly.TryParse(o[..10], out var d) && d <= today
                    ? $"<lastmod>{d:yyyy-MM-dd}</lastmod>" : "";

            foreach (var r in ctx.Registry.Values)
            {
                var (rows, _) = r.Catalogue(new FilterSet(null, null, null, null), null,
                                            CatalogueOrder.Name, 20000, 0);
                foreach (var w in rows)
                    sb.Append($"<url><loc>{ctx.PublicBase}/{w.Collection}/{w.GroupKey}</loc>"
                              + Lastmod(w.LastObserved)
                              + "<changefreq>monthly</changefreq><priority>0.6</priority></url>");

                // The version pages, which is where the law actually is.
                //
                // These were left out at first as "near-duplicates of one another that the work
                // page links anyway". That was wrong twice over. They are not duplicates: each is
                // a distinct legal state with different text, and each already canonicalises to
                // the date its own interval starts, so the set below is exactly the set of
                // canonical addresses. And they are not incidental: a work page is 553 words of
                // navigation, while a version page is the tens of thousands of words of law that
                // a reader searched for. Submitting the index and withholding the content is the
                // wrong way round.
                foreach (var (collection, groupKey, validFrom, observed) in r.VersionPaths())
                    sb.Append($"<url><loc>{ctx.PublicBase}/{collection}/{groupKey}/{validFrom}</loc>"
                              + Lastmod(observed)
                              + "<changefreq>yearly</changefreq><priority>0.5</priority></url>");
            }
            sb.Append("</urlset>");
            return Results.Content(sb.ToString(), "application/xml");
        });

        // ---- /ask playground: chat over the MCP tools, grounded and capped ----
        app.MapGet("/ask", () => Results.Redirect("/"));

        app.MapPost("/api/ask", async (HttpRequest req) =>
        {
            if (req.ContentLength is > 65536) return Results.Json(new { error = "Request too large." }, statusCode: 413);
            JsonNode? parsed;
            try { using var sr = new StreamReader(req.Body); parsed = JsonNode.Parse(await sr.ReadToEndAsync()); }
            catch { return Results.Json(new { error = "Bad JSON." }, statusCode: 400); }
            if (parsed?["messages"] is not JsonArray history)
                return Results.Json(new { error = "Body must be {\"messages\": [...]}." }, statusCode: 400);
            // Last X-Forwarded-For element: appended by our ingress, not spoofable by the client
            // (the first element is client-controlled and would reset the per-IP cap).
            var ip = req.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[^1].Trim()
                     ?? req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            var (status, bodyJson) = await askService.AskAsync(history, ip, req.Host.Value ?? "law.soufien.lu",
                req.HttpContext.RequestAborted);
            return Results.Content(bodyJson.ToJsonString(), "application/json", statusCode: status);
        });

        // ---- /api/ask/stream: the same answer, but the wait carries information.
        // A grounded answer takes 30-70s. CHI '26 (N=45, 26s and 45s waits) found that filling that
        // time with updates NAMING REAL OBJECTS beats a spinner on perceived speed, trust and load —
        // and the benefit grows with the wait. So this streams what the agent FOUND, never what it is
        // doing: "Code du travail — 3 articles as in force on 2019-03-01", not "searching…".
        app.MapPost("/api/ask/stream", async (HttpRequest req, HttpResponse res) =>
        {
            if (req.ContentLength is > 65536) { res.StatusCode = 413; return; }
            JsonNode? parsed;
            try { using var sr = new StreamReader(req.Body); parsed = JsonNode.Parse(await sr.ReadToEndAsync()); }
            catch { res.StatusCode = 400; return; }
            if (parsed?["messages"] is not JsonArray history) { res.StatusCode = 400; return; }
            var ip = req.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[^1].Trim()
                     ?? req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            res.Headers.ContentType = "text/event-stream";
            res.Headers.CacheControl = "no-cache";
            res.Headers["X-Accel-Buffering"] = "no";

            var writes = new SemaphoreSlim(1, 1);
            async Task Send(string ev, JsonNode data)
            {
                await writes.WaitAsync();
                try
                {
                    await res.WriteAsync($"event: {ev}\ndata: {data.ToJsonString()}\n\n", req.HttpContext.RequestAborted);
                    await res.Body.FlushAsync(req.HttpContext.RequestAborted);
                }
                catch { /* reader gone; the loop notices via the cancellation token */ }
                finally { writes.Release(); }
            }

            var steps = 0;
            var (status, bodyJson) = await askService.AskAsync(history, ip, req.Host.Value ?? "law.soufien.lu",
                req.HttpContext.RequestAborted,
                step =>
                {
                    Interlocked.Increment(ref steps);
                    _ = Send("step", new JsonObject
                    {
                        ["kind"] = step.Kind, ["text"] = step.Text,
                        ["work"] = step.Work, ["date"] = step.Date, ["anchor"] = step.Anchor,
                    });
                });

            // Outcome-aware (the labor illusion REVERSES on a weak result): a transparent wait ending
            // in a poor answer scored below delivering that same answer instantly. A refusal therefore
            // keeps its steps out of the transcript.
            bodyJson["narrated"] = status == 200 && bodyJson["ui"]?["gap"] is null && steps > 0;
            await Send("done", bodyJson);
        });

        app.MapGet("/healthz", () => Results.Text("ok"));
        app.MapGet("/readyz", () =>
        {
            var report = ctx.Registry.Readiness(ctx.Options);
            return Results.Json(report, statusCode: report.Ready ? 200 : 503);
        });

        app.MapGet("/provenance/{*key}", (string key) =>
        {
            foreach (var r in readers.Values)
            {
                var d = r.ByKey(key);
                if (d is null) continue;
                var events = r.Events(key);
                var sb = new StringBuilder();
                sb.Append($"""
                    <div class="card"><table class="kv">
                    <tr><td>lex_id</td><td class="mono">{H(d.Key)}</td></tr>
                    <tr><td>work identifier</td><td class="mono">{H(d.GroupIdentifier)}</td></tr>
                    <tr><td>record sha256</td><td class="mono">{H(d.RecordSha)}</td></tr>
                    <tr><td>source</td><td><a href="{H(d.SourceUri)}">{H(d.SourceUri)}</a></td></tr>
                    <tr><td>first observed</td><td class="mono">{H(d.ObservedFrom)}</td></tr>
                    <tr><td>valid (publisher-asserted)</td><td class="mono">{Interval(d)}</td></tr>
                    </table></div>
                    <h2>Event chain</h2><div class="card"><table><tr><th>event</th><th>observed</th><th>detail</th></tr>
                    """);
                foreach (var e in events)
                    sb.Append($"<tr><td>{H(e.Event)}</td><td class=\"mono\">{H(e.ObservedFrom)}</td><td>{H(e.Detail)}</td></tr>");
                sb.Append("</table></div>");
                sb.Append("""
                    <p class="sub">The record hash covers the canonical metadata record (no body text is stored in this mode).
                    Published hashes are forward-verifiable commitments: the day text serving is enabled, body hashes join this chain.</p>
                    """);
                sb.Append(EnvelopeCard(r, false));
                return Results.Content(Page("Provenance", sb.ToString(), H(DocTitle(d))), "text/html");
            }
            return Results.Content(Page("Provenance", "<p>Unknown lex_id.</p>"), "text/html", statusCode: 404);
        });

        return app;
    }
}
