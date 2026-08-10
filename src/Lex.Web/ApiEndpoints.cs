using System.Text;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Lex.Ask;
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
        var askRequests = ctx.AskRequests;
        static string ClientAddress(HttpRequest request) =>
            request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[^1].Trim()
            ?? request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        static string Fingerprint(byte[] body) =>
            Convert.ToHexStringLower(SHA256.HashData(body));
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

        app.MapPost("/api/ask", async (HttpRequest req, HttpResponse res) =>
        {
            if (req.ContentLength is > 65536) return Results.Json(new { error = "Request too large." }, statusCode: 413);
            var body = await BoundedRequestBody.ReadAsync(req.Body, 65536, req.HttpContext.RequestAborted);
            if (body is null) return Results.Json(new { error = "Request too large." }, statusCode: 413);
            JsonNode? parsed;
            try { parsed = JsonNode.Parse(body); }
            catch { return Results.Json(new { error = "Bad JSON." }, statusCode: 400); }
            if (parsed?["messages"] is not JsonArray history)
                return Results.Json(new { error = "Body must be {\"messages\": [...]}." }, statusCode: 400);
            if (!TryIdempotencyKey(req.Headers, out var idempotencyKey))
                return Results.Json(new { error = "Invalid Idempotency-Key." }, statusCode: 400);
            // Last X-Forwarded-For element: appended by our ingress, not spoofable by the client
            // (the first element is client-controlled and would reset the per-IP cap).
            var ip = ClientAddress(req);
            var claim = askRequests.Claim(ip, idempotencyKey, Fingerprint(body));
            var requestId = claim.RequestId;
            res.Headers["X-Lex-Request-Id"] = requestId;
            if (claim.Kind != AskRequestClaimKind.Owner)
            {
                var replay = await claim.Completion.WaitAsync(req.HttpContext.RequestAborted);
                return Results.Content(replay.Body, "application/json", statusCode: replay.Status);
            }
            try
            {
                var outcome = await askService.AskAsync(history, ip,
                    req.Host.Value ?? "law.soufien.lu", req.HttpContext.RequestAborted,
                    requestId: requestId);
                var (status, bodyJson) = outcome;
                var json = bodyJson.ToJsonString();
                claim.Complete(status, json, outcome.RetainForReplay);
                return Results.Content(json, "application/json", statusCode: status);
            }
            catch
            {
                const string failure = "{\"error\":\"Unexpected error in the playground.\"}";
                claim.Complete(500, failure, retainForReplay: true);
                return Results.Content(failure, "application/json", statusCode: 500);
            }
        });

        // ---- /api/ask/stream: the same answer, but the wait carries information.
        // A grounded answer takes 30-70s. CHI '26 (N=45, 26s and 45s waits) found that filling that
        // time with updates NAMING REAL OBJECTS beats a spinner on perceived speed, trust and load —
        // and the benefit grows with the wait. So this streams what the agent FOUND, never what it is
        // doing: "Code du travail — 3 articles as in force on 2019-03-01", not "searching…".
        app.MapPost("/api/ask/stream", async (HttpRequest req, HttpResponse res) =>
        {
            if (req.Headers["X-Lex-Stream-Version"].FirstOrDefault() != "1")
            {
                res.StatusCode = 400;
                await res.WriteAsJsonAsync(new { error = "Unsupported assistant stream version." },
                    req.HttpContext.RequestAborted);
                return;
            }
            if (req.ContentLength is > 65536) { res.StatusCode = 413; return; }
            var body = await BoundedRequestBody.ReadAsync(req.Body, 65536, req.HttpContext.RequestAborted);
            if (body is null) { res.StatusCode = 413; return; }
            if (!TryIdempotencyKey(req.Headers, out var idempotencyKey)) { res.StatusCode = 400; return; }
            JsonNode? parsed;
            try { parsed = JsonNode.Parse(body); }
            catch { res.StatusCode = 400; return; }
            if (parsed?["messages"] is not JsonArray history) { res.StatusCode = 400; return; }
            var ip = ClientAddress(req);
            var claim = askRequests.Claim(ip, idempotencyKey, Fingerprint(body));
            var requestId = claim.RequestId;
            if (claim.Kind is AskRequestClaimKind.Conflict or AskRequestClaimKind.Busy
                or AskRequestClaimKind.ReplayUnavailable)
            {
                var rejected = await claim.Completion;
                res.StatusCode = rejected.Status;
                res.ContentType = "application/json";
                await res.WriteAsync(rejected.Body, req.HttpContext.RequestAborted);
                return;
            }

            void StreamHeaders()
            {
                res.Headers.ContentType = "text/event-stream";
                res.Headers.CacheControl = "no-cache";
                res.Headers["X-Accel-Buffering"] = "no";
                res.Headers["X-Lex-Request-Id"] = requestId;
            }
            StreamHeaders();

            var writes = new SemaphoreSlim(1, 1);
            var sequence = 0;
            async Task Send(string ev, JsonNode data)
            {
                await writes.WaitAsync();
                try
                {
                    var envelope = new JsonObject
                    {
                        ["version"] = "1",
                        ["request_id"] = requestId,
                        ["sequence"] = ++sequence,
                        ["payload"] = data,
                    };
                    await res.WriteAsync($"event: {ev}\ndata: {envelope.ToJsonString()}\n\n", req.HttpContext.RequestAborted);
                    await res.Body.FlushAsync(req.HttpContext.RequestAborted);
                }
                catch (OperationCanceledException) when (req.HttpContext.RequestAborted.IsCancellationRequested)
                { /* The reader left; AskAsync observes the same cancellation token. */ }
                catch (IOException)
                { /* The legal result remains authoritative when the SSE reader disconnects. */ }
                catch (ObjectDisposedException)
                { /* The response stream was closed after the request had already started. */ }
                finally { writes.Release(); }
            }

            if (claim.Kind == AskRequestClaimKind.Duplicate)
            {
                try
                {
                    var operationCount = 0;
                    await foreach (var operation in claim.OperationResults.ReadAllAsync(
                                       req.HttpContext.RequestAborted))
                    {
                        if (JsonNode.Parse(operation) is { } parsedOperation)
                        {
                            await Send("operation_result", parsedOperation);
                            operationCount++;
                        }
                    }
                    var replay = await claim.Completion.WaitAsync(req.HttpContext.RequestAborted);
                    var replayBody = JsonNode.Parse(replay.Body);
                    if (operationCount == 0 && replayBody?["operations"] is JsonArray operations)
                        foreach (var operation in operations)
                            if (operation is not null)
                                await Send("operation_result", operation.DeepClone());
                    if (replay.Status == 200)
                        await Send("done", replayBody ?? new JsonObject());
                    else
                        await Send("transport_error", new JsonObject
                        {
                            ["status"] = replay.Status,
                            ["error"] = replayBody?["error"]?.GetValue<string>()
                                ?? "The assistant request did not complete.",
                        });
                }
                finally
                {
                    claim.Unsubscribe();
                }
                return;
            }

            var steps = 0;
            var progress = new AskService.AskProgressCallbacks(
                Step: (step, _) =>
                {
                    Interlocked.Increment(ref steps);
                    return new ValueTask(Send("step", new JsonObject
                    {
                        ["kind"] = step.Kind, ["text"] = step.Text,
                        ["work"] = step.Work, ["date"] = step.Date, ["anchor"] = step.Anchor,
                    }));
                },
                OperationResult: async (operation, _) =>
                {
                    claim.ReportOperation(operation.ToJsonString());
                    await Send("operation_result", operation);
                },
                Synthesis: (status, _) => new ValueTask(Send("synthesis", new JsonObject
                {
                    ["status"] = status,
                })));
            AskService.AskOutcome outcome;
            try
            {
                outcome = await askService.AskAsync(history, ip,
                    req.Host.Value ?? "law.soufien.lu",
                    req.HttpContext.RequestAborted, progress, requestId);
            }
            catch (OperationCanceledException) when (req.HttpContext.RequestAborted.IsCancellationRequested)
            {
                const string cancelled = "{\"error\":\"The assistant request was cancelled.\"}";
                claim.Complete(499, cancelled, retainForReplay: true);
                return;
            }
            catch
            {
                const string failure = "{\"error\":\"Unexpected error in the playground.\"}";
                claim.Complete(500, failure, retainForReplay: true);
                await Send("transport_error", new JsonObject
                {
                    ["status"] = 500,
                    ["error"] = "Unexpected error in the playground.",
                });
                return;
            }
            var (status, bodyJson) = outcome;

            // Outcome-aware (the labor illusion REVERSES on a weak result): a transparent wait ending
            // in a poor answer scored below delivering that same answer instantly. A refusal therefore
            // keeps its steps out of the transcript.
            bodyJson["narrated"] = status == 200 && bodyJson["ui"]?["gap"] is null && steps > 0;
            claim.Complete(status, bodyJson.ToJsonString(), outcome.RetainForReplay);
            if (status == 200)
                await Send("done", bodyJson);
            else
                await Send("transport_error", new JsonObject
                {
                    ["status"] = status,
                    ["error"] = bodyJson["error"]?.GetValue<string>()
                        ?? "The assistant request did not complete.",
                });
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

    internal static bool TryIdempotencyKey(IHeaderDictionary headers, out string key)
    {
        if (!headers.TryGetValue("Idempotency-Key", out var values))
        {
            key = Guid.NewGuid().ToString("N");
            return true;
        }
        var supplied = values.Count == 1 ? values[0] : null;
        if (string.IsNullOrEmpty(supplied)
            || supplied.Length > 128
            || supplied.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')))
        {
            key = "";
            return false;
        }
        key = supplied;
        return true;
    }
}
