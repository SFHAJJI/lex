using System.Text;
using System.Text.Json.Nodes;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Lex.Ask;
using Lex.Index;
using Lex.Mcp;
using Lex.Web;
using static Lex.Web.PageShell;
using static Lex.Web.Fragments;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// ---- composition root ------------------------------------------------------------------
//
// Configuration was seven Environment.GetEnvironmentVariable calls spread through this file,
// each with its own inline default and none of them validated. It is now one bound, validated
// object, and the index registry is a service rather than a local dictionary captured by every
// route lambda in the file.
var options = LexOptionsSetup.FromEnvironment(builder.Environment);
builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(options));
builder.Services.AddSingleton<IndexRegistry>();
builder.Services.AddSingleton(sp => new McpCore(sp.GetRequiredService<IndexRegistry>().All));

// Foundry-hybrid observability (D45 posture: keep the loop, adopt the platform's
// tracing): OpenTelemetry via the Azure Monitor distro, exporting to the App
// Insights resource a Foundry project can attach to. Enabled only when the
// connection string is configured; the app is fully functional without it.
if (!string.IsNullOrEmpty(options.AppInsightsConnectionString))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
    builder.Services.ConfigureOpenTelemetryTracerProvider((_, b) => b.AddSource(AskService.ActivitySourceName));
}

var app = builder.Build();
app.UseStaticFiles();   // wwwroot: og.png (social preview card)

var publicBase = options.PublicBase;
var registry = app.Services.GetRequiredService<IndexRegistry>();
var readers = registry.All;
var indexDir = options.IndexDir;

var mcpCore = app.Services.GetRequiredService<McpCore>();
var askService = new AskService(mcpCore);
Console.Error.WriteLine($"[web] /ask playground: {(askService.Enabled ? "enabled" : "disabled (no AOAI_ENDPOINT/AOAI_KEY)")}");

// The shell, the stylesheet and HTML-encoding now live in PageShell. These two locals keep
// every existing call site unchanged, which is what made the move verifiable in one step.
string Page(string title, string body, string? subtitle = null, string nav = "", string? h1 = null)
    => PageShell.Page(publicBase, title, body, subtitle, nav, h1);

LexIndexReader? Reader(string publisher) => readers.GetValueOrDefault(publisher);

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

app.MapGet("/", () =>
{
    // Counts come from the mounted indexes at render time, never hand-written numbers.
    var cov = readers.Values.Select(r => r.Coverage()).ToList();
    var tools = mcpCore.ToolDefs().OfType<JsonObject>()
                       .Select(t => t["name"]!.GetValue<string>()).ToList();

    // The suggested starting points, checked against the index that will serve them.
    //
    // These lived in the workspace bundle as hand-written slugs, and one of the three was
    // "lu-legilux:code-penal", which does not exist: the Code penal is loi-1879-06-18-n1. So a
    // first-time visitor who took one of only three invitations on the page was told the work was
    // unknown. Worse, that work is the best thing in the corpus to land on, 699 provisions with
    // per-article permalinks. Emitting them from here means a door that does not resolve is
    // dropped before anyone can click it, and says so in the log.
    var doors = new (string Publisher, string Work, string Label)[]
    {
        ("lu-legilux", "constitution-1868-10-17-n1", "The Constitution"),
        ("lu-legilux", "loi-2006-07-31-n2", "Code du travail"),
        ("lu-legilux", "loi-1879-06-18-n1", "Code pénal"),
    };
    var liveDoors = new JsonArray();
    foreach (var (pub, work, label) in doors)
    {
        if (readers.TryGetValue(pub, out var dr) && dr.WorkExists(work))
            liveDoors.Add(new JsonObject { ["work"] = $"{pub}:{work}", ["label"] = label });
        else
            Console.Error.WriteLine($"[web] door dropped: {pub}:{work} ({label}) is not in a mounted index");
    }
    // The thesis of the whole project, said once, at the top. It used to sit below three
    // promotional cards, where the visitors most likely to bounce never reached it — while
    // the sentence above the fold merely announced that the site answers questions.
    var body = $"""
        <p class="lede">Ask what any Luxembourg law said on any day, exactly as its publisher
        issued it. Plus ten EU acts, from the GDPR to the AI Act.</p>
        """
        + $"""
        <!-- Read synchronously by the workspace on mount, so the doors never flash in or need a
             round trip of their own. -->
        <script type="application/json" id="doors">{liveDoors.ToJsonString()}</script>
        """
        + """
        <!-- The workspace mounts here. Without JavaScript the page still explains itself
             and every permalink below still works, those are server-rendered documents. -->
        <div id="workspace"><noscript><p class="sub">The interactive workspace needs JavaScript.
          Everything is also reachable as plain pages: <a href="/find">find a law</a>,
          <a href="/changed">what changed</a>, <a href="/stories">stories</a>.</p></noscript></div>
        """
        + $"""
        <div class="frontdoor">
        <p class="sub">
        <span class="badge">{cov.Sum(c => c.Groups):n0} laws</span>
        <span class="badge">{cov.Sum(c => c.Rows):n0} dated versions</span>
        <span class="badge">{cov.Sum(c => c.TextServed):n0} with full text</span>
        <span class="badge">{H(cov.Select(c => c.EarliestValidFrom).Min())} → {H(cov.Select(c => c.LatestValidFrom).Max())}</span>
        <span class="badge ok">cryptographically signed</span></p>
        <p class="sub">Free assistant, daily limit. <a href="/ai">Connect your own AI</a> for
        unlimited use.</p>

        <!-- A fork, not a menu. Three readers arrive here and they want different things; the
             third one is the differentiator and was a footer link. A project that invites you to
             audit it is making a claim the other two doors cannot make for it. -->
        <nav class="fork" aria-label="Where to go next">
          <a href="/browse"><b>I want to read a law</b><span>The catalogue: every work, every dated version.</span></a>
          <a href="/developers"><b>I want to build on this</b><span>MCP endpoint, {tools.Count} tools, the datasets, the licence.</span></a>
          <a href="/coverage"><b>I want to check whether this is honest</b><span>What Lex holds, and what it knowably lacks.</span></a>
        </nav>
        </div>
        """
        + """
        <style>
          .lede { font-size:18px; color:var(--muted); margin:0 0 22px; max-width:74ch }
          .lede b { color:var(--fg); font-variant-numeric:tabular-nums }
          .fork { display:grid; grid-template-columns:repeat(auto-fit,minmax(230px,1fr)); gap:10px; margin:18px 0 0 }
          .fork a { display:block; border:1px solid var(--line); border-radius:10px; padding:12px 14px;
                    background:var(--card); text-decoration:none; color:inherit }
          .fork a:hover { border-color:var(--accent); text-decoration:none }
          .fork b { display:block; color:var(--accent); font-size:15px }
          .fork span { display:block; color:var(--muted); font-size:13.5px; margin-top:3px }
          /* Once a law, a period or a search is loaded, the front-door content is noise.
             The workspace sets data-workspace on <body>; everything promotional steps aside. */
          body[data-workspace="active"] .frontdoor,
          body[data-workspace="active"] .lede,
          body[data-workspace="active"] main > h1 { display:none }
        </style>
        <link rel="stylesheet" href="/app/workspace.css">
        <script type="module" src="/app/workspace.js"></script>
        """;
    return Results.Content(Page("Luxembourg law as it stood on any date, plus ten EU acts", body, null, "ask",
        h1: "A law is not one document."), "text/html");
});

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

// ---- endpoint modules ---------------------------------------------------------------------
//
// The routes were twenty-nine lambdas in this file, each closing over whatever happened to be in
// scope, which is what made it two thousand lines and impossible to move a piece of. Grouped by
// the question each one answers, and handed one explicit context instead of an implicit closure.
var ctx = new WebContext(registry, options, mcpCore, askService);
app.MapExplainers(ctx)
   .MapCatalogue(ctx);

// ---- /browse: the catalogue.
//
// It was two source cards, eight curated links and a search box, under a header item that says
// "Browse everything" and a link on /find that says "Open the catalogue". Seven links against
// 1,409 works is a dead end at the most prominent invitation on the site, and it breaks the first
// promise the navigation makes. Every column and every filter below already existed in the signed
// index; nothing new is derived, it was simply never asked for.






















// ---- version rail: the versions of a work as marks on a time axis. Replaces a list of
// links with a shape you can read — clustering IS the amendment history.



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

// Only mounted publishers own the /{publisher}/... space. WebApplication inserts routing
// at the START of the pipeline, so an unconstrained /{publisher}/{work} would match
// /app/workspace.js and static files would stand down for an already-selected endpoint.
var pubRoute = $"{{publisher:regex(^({string.Join("|", readers.Keys.Select(System.Text.RegularExpressions.Regex.Escape))})$)}}";

app.MapGet($"/{pubRoute}/{{work}}/diff/{{dateA}}/{{dateB}}", (string publisher, string work, string dateA, string dateB) =>
{
    var r = Reader(publisher);
    if (r is null) return Results.Content(Page("Unknown publisher", $"<p>No index mounted for <b>{H(publisher)}</b>.</p>"), "text/html", statusCode: 404);
    if (!DateOnly.TryParse(dateA, out var da) || !DateOnly.TryParse(dateB, out var db2))
        return Results.Content(Page("Bad date", "<p>Use YYYY-MM-DD for both dates.</p>"), "text/html", statusCode: 400);

    var a = r.AsOf(work, da, FilterSet.All);
    var b = r.AsOf(work, db2, FilterSet.All);
    if (a is null || b is null)
        return Results.Content(Page("No version for date",
            $"<p>status <span class=\"mono\">no_version_for_date</span>, resolved: {da:yyyy-MM-dd}={(a is not null)}, {db2:yyyy-MM-dd}={(b is not null)}. See the <a href=\"/{H(publisher)}/{H(work)}\">timeline</a>.</p>"),
            "text/html", statusCode: 404);

    var sb = new StringBuilder();
    sb.Append($"""
        <div class="card"><table class="kv">
        <tr><td>on {da:yyyy-MM-dd}</td><td class="mono"><a href="/{H(publisher)}/{H(work)}/{da:yyyy-MM-dd}">{H(a.Key)}</a> ({Interval(a)})</td></tr>
        <tr><td>on {db2:yyyy-MM-dd}</td><td class="mono"><a href="/{H(publisher)}/{H(work)}/{db2:yyyy-MM-dd}">{H(b.Key)}</a> ({Interval(b)})</td></tr>
        </table></div>
        """);

    if (a.Key == b.Key)
        sb.Append("<div class=\"notice\"><b>No change.</b> The same version applied on both dates.</div>");
    else if (a.TextPublic && b.TextPublic
             && r.BuildBody(a) is { } bodyA && r.BuildBody(b) is { } bodyB)
        sb.Append(RenderDiff(bodyA, bodyB));
    else
        sb.Append($"""
            <div class="notice"><b>Different versions applied</b>, but a text diff is unavailable here
            (status <span class="mono">text_withheld</span>). Compare at the official source:
            <a href="{H(a.SourceUri)}">version of {H(a.ValidFrom)}</a> vs
            <a href="{H(b.SourceUri)}">version of {H(b.ValidFrom)}</a>.</div>
            """);
    sb.Append(EnvelopeCard(r, IsProvisional(r, db2)));
    return Results.Content(Page($"What changed, {H(DocTitle(b))}", sb.ToString(),
        $"{da:yyyy-MM-dd} → {db2:yyyy-MM-dd} · no interpretation, just the text delta"), "text/html");
});

app.MapGet($"/{pubRoute}/{{work}}", (string publisher, string work) =>
{
    var r = Reader(publisher);
    if (r is null) return Results.Content(Page("Unknown publisher", $"<p>No index mounted for <b>{H(publisher)}</b>. See <a href=\"/coverage\">coverage</a>.</p>"), "text/html", statusCode: 404);
    var rows = r.Timeline(work);
    if (rows.Count == 0)
        return Results.Content(Page("Unknown work", $"<p>status <span class=\"mono\">unknown_work</span>, no work <b>{H(work)}</b> in {H(publisher)}. Try <a href=\"/search\">search</a>.</p>"), "text/html", statusCode: 404);

    var t = DocTitle(rows[^1]);
    var sb = new StringBuilder();
    sb.Append($"<p><span class=\"badge\">{H(rows[^1].Kind)}</span> <span class=\"badge\">{rows.Count} version(s)</span> <a class=\"badge\" href=\"{H(rows[^1].SourceUri)}\">official text ↗</a></p>");
    sb.Append(VersionRail(publisher, work, rows, null));
    sb.Append($"<p><a href=\"/{H(publisher)}/{H(work)}/{H(rows[^1].ValidFrom)}\"><b>Read the current text →</b></a></p>");
    sb.Append("<details class=\"card\"><summary>Every version as a table</summary><table><tr><th>valid</th><th>as-of view</th><th>status</th><th>provenance</th></tr>");
    foreach (var v in rows)
        sb.Append($"""
            <tr><td class="mono">{Interval(v)}</td>
            <td><a href="/{H(publisher)}/{H(work)}/{H(v.ValidFrom)}">as of {H(v.ValidFrom)}</a></td>
            <td>{(v.ValidTo is null ? "<span class=\"badge ok\">open</span>" : "<span class=\"badge\">superseded</span>")}</td>
            <td><a class="mono" href="/provenance/{H(v.Key)}">{H(v.Key.Split(':')[^1])}</a></td></tr>
            """);
    sb.Append("</table></details>");
    sb.Append("<p class=\"sub\">Every state this document has been in, as asserted by the publisher. The corpus repo's <span class=\"mono\">git log</span> for this work shows the same history.</p>");
    sb.Append(EnvelopeCard(r, false));
    return Results.Content(Page(H(t), sb.ToString(), $"every version, on a time axis", "find"), "text/html");
});

app.MapGet($"/{pubRoute}/{{work}}/{{date}}", (string publisher, string work, string date) =>
{
    var r = Reader(publisher);
    if (r is null) return Results.Content(Page("Unknown publisher", $"<p>No index mounted for <b>{H(publisher)}</b>.</p>"), "text/html", statusCode: 404);
    if (!DateOnly.TryParse(date, out var d))
        return Results.Content(Page("Bad date", $"<p>'{H(date)}' is not a date (use YYYY-MM-DD).</p>"), "text/html", statusCode: 400);

    var doc = r.AsOf(work, d, FilterSet.All);
    if (doc is null)
    {
        if (!r.WorkExists(work))
            return Results.Content(Page("Unknown work", $"<p>status <span class=\"mono\">unknown_work</span>, no work <b>{H(work)}</b>. Try <a href=\"/search\">search</a>.</p>"), "text/html", statusCode: 404);
        var timeline = r.Timeline(work);
        var sb0 = new StringBuilder();
        sb0.Append($"""
            <div class="notice">status <span class="mono">no_version_for_date</span>, the work exists, but no
            version covers <b>{d:yyyy-MM-dd}</b>. The publisher's digitised history for this work covers:</div>
            """);
        sb0.Append("<ul>");
        foreach (var v in timeline.Take(30))
            sb0.Append($"<li><a href=\"/{H(publisher)}/{H(work)}/{H(v.ValidFrom)}\" class=\"mono\">{Interval(v)}</a></li>");
        sb0.Append("</ul>");
        sb0.Append(EnvelopeCard(r, IsProvisional(r, d)));
        return Results.Content(Page(H(work), sb0.ToString(), $"as of {d:yyyy-MM-dd}, honest refusal"), "text/html", statusCode: 404);
    }

    var all = r.Timeline(work);
    var idx = all.FindIndex(x => x.Key == doc.Key && x.Language == doc.Language);
    var prev = idx > 0 ? all[idx - 1] : null;
    var next = idx >= 0 && idx < all.Count - 1 ? all[idx + 1] : null;

    var sb = new StringBuilder();
    sb.Append(VersionRail(publisher, work, all, doc.ValidFrom));
    // Unambiguous temporal-status banner (the legislation.gov.uk precedent): the reader
    // must never wonder WHICH state of the law they are looking at.
    sb.Append(next is not null
        ? $"""
           <div class="notice"><b>Point-in-time view as at {d:yyyy-MM-dd}.</b> This version has been
           <b>superseded</b>, it applied {H(Interval(doc))}. <a href="/{H(publisher)}/{H(work)}">Jump to the
           version in force today</a> or <a href="/{H(publisher)}/{H(work)}/diff/{H(doc.ValidFrom)}/{H(next.ValidFrom)}">see
           exactly what changed next</a>.</div>
           """
        : $"""
           <div class="notice" style="border-left-color:var(--ok)"><b>Point-in-time view as at {d:yyyy-MM-dd}.</b>
           This is the latest state the publisher has consolidated, valid {H(Interval(doc))}.</div>
           """);
    // Most readers arrive from a search engine straight onto this page and never see the
    // homepage. The two things they must know — what a consolidated text is, and that it
    // carries no legal force — belong here, in plain words, not only on the front door.
    // Collapsed, and below the law: a reader who came for Article 12 should meet Article 12.
    var primer = """
        <details class="card"><summary><b>New here? What am I looking at?</b></summary>
        <p>This is a <b>consolidated</b> text: the original law with every later amendment merged in,
        as the official publisher produced it for a given date. Laws are amended constantly, so
        <b>“the law” has no single text, only a text per date</b>. That date is the banner above.</p>
        <p><b>It has no legal force.</b> Only the version published in the official gazette
        (<i>Mémorial</i> / Official Journal) is authentic, the publishers say so themselves, and so do we.
        Lex reproduces their text without altering a byte, and links the source on every page.
        This is legal <i>information</i>, never legal advice: it reports what the text said,
        never what it means for your situation.</p>
        <p class="sub">“Valid from → to” = the window in which this text applied.
        “Open” = still current as far as the publisher has consolidated.
        Each article carries its own hash so you can prove it was not tampered with , 
        <a href="/verify">here is how</a>.</p></details>
        """;
    var record = $"""
        <table class="kv">
        <tr><td>as of</td><td><b>{d:yyyy-MM-dd}</b> → this version applied</td></tr>
        <tr><td>valid</td><td class="mono">{Interval(doc)} <span class="badge">{H(doc.ValidTimeSource)}-asserted</span></td></tr>
        <tr><td>type</td><td><span class="badge">{H(doc.Kind)}</span> {H(doc.Title ?? "")}</td></tr>
        <tr><td>language</td><td>{H(doc.Language)}</td></tr>
        {(doc.PublicationDate is null ? "" : $"<tr><td>published</td><td class=\"mono\">{H(doc.PublicationDate)}</td></tr>")}
        <tr><td>lex_id</td><td class="mono"><a href="/provenance/{H(doc.Key)}">{H(doc.Key)}</a></td></tr>
        <tr><td>record sha256</td><td class="mono">{H(doc.RecordSha)}</td></tr>
        </table>
        """;

    var provisions = doc.TextPublic ? r.Provisions(LexIndexReader.RidOf(doc)) : [];
    if (provisions.Count > 0)
    {
        sb.Append($"""
            <div class="notice" style="border-left-color:var(--ok)"><b>Text included, per-article reading view.</b>
            Deterministic extraction of the verbatim retrieved document; each article carries its own hash and anchor.
            {H(r.Stamp.GetValueOrDefault("attribution"))}</div>
            <details class="card"><summary><b>Outline, {provisions.Count} provisions</b></summary><p>
            """);
        foreach (var p in provisions)
            sb.Append($"<a href=\"#{H(p.Anchor)}\" class=\"badge\">{H(p.Num ?? p.Heading ?? p.Anchor)}</a> ");
        sb.Append("</p></details>");

        string? lastPath = null;
        var shown = 0;
        foreach (var p in provisions)
        {
            if (p.Path is not null && p.Path != lastPath)
            {
                sb.Append($"<h2 style=\"margin-top:26px\">{H(p.Path)}</h2>");
                lastPath = p.Path;
            }
            var title = p.Num is null && p.Heading is null ? p.Anchor
                : string.Join(", ", new[] { p.Num, p.Heading }.Where(s => !string.IsNullOrEmpty(s)));
            sb.Append($"""
                <div class="card" id="{H(p.Anchor)}">
                <b>{H(title)}</b>
                <a class="sub mono" href="#{H(p.Anchor)}" title="permalink to this provision">#{H(p.Anchor)}</a>
                {(p.ArticleValidFrom is not null && p.ArticleValidFrom != doc.ValidFrom ? $"<span class=\"badge\">applicable {H(p.ArticleValidFrom)}</span>" : "")}
                <pre style="white-space:pre-wrap;font:14px/1.65 Georgia,'Times New Roman',serif;margin:8px 0 0">{H(p.TextMd)}</pre>
                </div>
                """);
            shown++;
            if (shown >= 400) { sb.Append($"<p class=\"sub\">,  {provisions.Count - shown:n0} further provisions omitted from this view; retrieve them via the MCP tools , </p>"); break; }
        }
        // The receipt, after the goods. This table was the first thing on the page, above the law
        // it describes, which is backwards for the nineteen readers in twenty who came to read the
        // law. It is still one click away for the twentieth, who is the reason it exists.
        sb.Append($"""
            <details class="card"><summary><b>Provenance and validity</b>
            <span class="sub">dates, identifier, hash</span></summary>{record}</details>
            """);
    }
    else
    {
        // No wording is held, so the record is not a receipt for the answer, it IS the answer.
        // Hiding it here would leave the page with nothing on it.
        sb.Append(TextWithheldBox(doc));
        sb.Append($"""<div class="card">{record}</div>""");
    }
    sb.Append(primer);
    sb.Append("<p>");
    if (prev is not null)
    {
        sb.Append($"<a href=\"/{H(publisher)}/{H(work)}/{H(prev.ValidFrom)}\">← previous version ({H(prev.ValidFrom)})</a> &nbsp;&nbsp;");
        if (doc.TextPublic) sb.Append($"<a href=\"/{H(publisher)}/{H(work)}/diff/{H(prev.ValidFrom)}/{H(doc.ValidFrom)}\">what changed?</a> &nbsp;&nbsp;");
    }
    sb.Append($"<a href=\"/{H(publisher)}/{H(work)}\">timeline</a>");
    if (next is not null) sb.Append($" &nbsp;&nbsp;<a href=\"/{H(publisher)}/{H(work)}/{H(next.ValidFrom)}\">next version ({H(next.ValidFrom)}) →</a>");
    sb.Append("</p>");
    sb.Append(EnvelopeCard(r, IsProvisional(r, d)));
    return Results.Content(Page(H(DocTitle(doc)), sb.ToString(), $"as it stood on {d:yyyy-MM-dd}, permalink: /{H(publisher)}/{H(work)}/{d:yyyy-MM-dd}"), "text/html");
});

app.Run();

// Top-level statements compile to an internal Program class. WebApplicationFactory<T> needs a
// type from this assembly to boot the app in-process, which is how the golden tests render every
// route without shelling out to a server.
public partial class Program;
