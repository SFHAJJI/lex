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
        var mcpCore = ctx.Mcp;
        var askService = ctx.Ask;
        string Page(string title, string body, string? subtitle = null, string nav = "", string? h1 = null)
            => PageShell.Page(ctx.PublicBase, title, body, subtitle, nav, h1);
        LexIndexReader? Reader(string publisher) => ctx.Registry.All.GetValueOrDefault(publisher);

        app.MapGet("/robots.txt", () => Results.Text("User-agent: *\nAllow: /\n"));

        // ---- public MCP endpoint (Streamable HTTP, stateless): any MCP client can connect ----
        app.MapPost("/mcp", async (HttpRequest req) =>
        {
            using var sr = new StreamReader(req.Body);
            var body = await sr.ReadToEndAsync();
            JsonNode? msg;
            try { msg = JsonNode.Parse(body); } catch { return Results.BadRequest(); }
            if (msg is JsonArray batch)
            {
                var responses = new JsonArray();
                foreach (var m in batch.ToArray())
                    if (m is not null && mcpCore.HandleMessage(m) is { } r) responses.Add(r);
                return responses.Count == 0 ? Results.Accepted() : Results.Json(responses);
            }
            if (msg is null) return Results.BadRequest();
            var resp = mcpCore.HandleMessage(msg);
            return resp is null ? Results.Accepted() : Results.Json(resp);
        });

        app.MapGet("/mcp", () => Results.Text("POST JSON-RPC here (MCP Streamable HTTP). Connect: claude mcp add --transport http lex <this URL>", statusCode: 405));

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
