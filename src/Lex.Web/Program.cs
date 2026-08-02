using System.Text;
using System.Text.Json.Nodes;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Lex.Ask;
using Lex.Index;
using Lex.Mcp;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Foundry-hybrid observability (D45 posture: keep the loop, adopt the platform's
// tracing): OpenTelemetry via the Azure Monitor distro, exporting to the App
// Insights resource a Foundry project can attach to. Enabled only when the
// connection string is configured; the app is fully functional without it.
if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
    builder.Services.ConfigureOpenTelemetryTracerProvider((_, b) => b.AddSource(AskService.ActivitySourceName));
}

var app = builder.Build();
app.UseStaticFiles();   // wwwroot: og.png (social preview card)

// Absolute base for social-preview metadata; falls back to the canonical host so a
// pasted link previews correctly even when the env var is unset.
var publicBase = Environment.GetEnvironmentVariable("LEX_PUBLIC_BASE_URL")?.TrimEnd('/')
                 ?? "https://law.soufien.lu";

// ---- index registry: one LexIndexReader per mounted per-publisher index file (D27) ----
var indexDir = Environment.GetEnvironmentVariable("LEX_INDEX_DIR")
               ?? Path.Combine(app.Environment.ContentRootPath, "indexes");
var readers = new Dictionary<string, LexIndexReader>(StringComparer.Ordinal);
if (Directory.Exists(indexDir))
    foreach (var db in Directory.EnumerateFiles(indexDir, "index-*.db"))
    {
        var r = LexIndexReader.Open(db);
        readers[r.Collection] = r;
        Console.Error.WriteLine($"[web] mounted {db} ({r.Collection}, signature_valid={r.SignatureValid})");
    }
if (readers.Count == 0) Console.Error.WriteLine($"[web] WARNING: no indexes found in {indexDir}");

var mcpCore = new McpCore(readers);
var askService = new AskService(mcpCore);
Console.Error.WriteLine($"[web] /ask playground: {(askService.Enabled ? "enabled" : "disabled (no AOAI_ENDPOINT/AOAI_KEY)")}");

string H(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");

string Page(string title, string body, string? subtitle = null, string nav = "") => $$"""
    <!DOCTYPE html>
    <html lang="en">
    <head>
    <meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
    <title>{{H(title)}} — Lex</title>
    <meta name="description" content="Point-in-time Luxembourg + EU law: what did the rule say on a given date? Grounded AI answers, permalinks, timelines, diffs, cryptographic provenance, and a public MCP endpoint.">
    <meta property="og:title" content="{{H(title)}} — Lex">
    <meta property="og:description" content="Point-in-time Luxembourg + EU law with grounded AI answers, per-article history, and verifiable provenance.">
    <meta property="og:type" content="website">
    <meta property="og:site_name" content="Lex">
    <meta property="og:image" content="{{publicBase}}/og.png">
    <meta property="og:image:width" content="1200"><meta property="og:image:height" content="630">
    <meta name="twitter:card" content="summary_large_image">
    <meta name="twitter:image" content="{{publicBase}}/og.png">
    <link rel="icon" href="data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 100 100'%3E%3Ctext y='.9em' font-size='90'%3E%E2%9A%96%3C/text%3E%3C/svg%3E">
    <style>
      /* Law is set in a serif; the interface around it is sans. The accent is reserved
         for one thing only: the date you are looking at. */
      :root { --bg:#faf8f3; --fg:#15171b; --muted:#5d5a53; --line:#e2ded4; --accent:#a8342a;
              --card:#ffffff; --ok:#2f6b46; --warn:#8a5a12; --on-accent:#ffffff;
              --serif:Georgia,'Iowan Old Style','Palatino Linotype','Times New Roman',serif;
              --mono:ui-monospace,'Cascadia Code',Consolas,monospace; }
      @media (prefers-color-scheme: dark) {
        :root { --bg:#101215; --fg:#e9e5dd; --muted:#9a958b; --line:#2b2e34; --accent:#e0705f;
                --card:#171a1e; --ok:#6cc48f; --warn:#e0b070; --on-accent:#101215; } }
      * { box-sizing:border-box }
      body { margin:0; font:16px/1.6 ui-sans-serif,system-ui,-apple-system,'Segoe UI',Roboto,sans-serif; background:var(--bg); color:var(--fg); }
      a { color:var(--accent); text-decoration:none } a:hover { text-decoration:underline }
      header { border-bottom:1px solid var(--line); padding:14px 20px; display:flex; gap:20px; align-items:baseline; flex-wrap:wrap }
      header .brand { font-family:var(--serif); font-weight:700; font-size:21px; color:var(--fg); letter-spacing:-.01em }
      header a.navlink { color:var(--muted); font-size:14.5px }
      header a.navlink.on { color:var(--fg); font-weight:600; box-shadow:inset 0 -2px 0 var(--accent) }
      main { max-width:960px; margin:0 auto; padding:26px 20px 60px }
      h1 { font-family:var(--serif); font-size:29px; line-height:1.15; letter-spacing:-.015em; margin:0 0 6px; text-wrap:balance }
      h2 { font-family:var(--serif); font-size:20px; margin:28px 0 8px }
      .sub { color:var(--muted); margin:0 0 18px; font-size:14.5px }
      .card { background:var(--card); border:1px solid var(--line); border-radius:10px; padding:14px 16px; margin:12px 0; overflow-x:auto }
      .badge { display:inline-block; border:1px solid var(--line); border-radius:99px; padding:1px 10px; font-size:12.5px; color:var(--muted); margin-right:6px }
      .badge.ok { color:var(--ok); border-color:var(--ok) }
      .badge.warn { color:var(--warn); border-color:var(--warn) }
      table { border-collapse:collapse; width:100%; font-size:14.5px }
      th,td { text-align:left; padding:7px 10px; border-bottom:1px solid var(--line); vertical-align:top }
      th { color:var(--muted); font-weight:600; font-size:13px }
      .mono { font-family:var(--mono); font-size:13px; word-break:break-all }
      .kv td:first-child { color:var(--muted); white-space:nowrap; padding-right:18px }
      form.inline { display:flex; gap:8px; flex-wrap:wrap; margin:10px 0 }
      input,select,button { font:inherit; padding:7px 10px; border:1px solid var(--line); border-radius:8px; background:var(--bg); color:var(--fg) }
      button { background:var(--accent); color:var(--on-accent); border-color:var(--accent); cursor:pointer }
      footer { border-top:1px solid var(--line); margin-top:40px; padding:16px 20px; color:var(--muted); font-size:13px }
      .notice { border-left:3px solid var(--warn); padding:10px 14px; background:var(--card); border-radius:0 8px 8px 0; margin:12px 0; font-size:14.5px }
      .snippet { color:var(--muted); font-size:13.5px }
      .sitemap { margin-bottom:12px; line-height:2 }
      :focus-visible { outline:2px solid var(--accent); outline-offset:2px }
      .rail { position:relative; height:44px; margin:14px 0 0 }
      .rail .axis { position:absolute; left:0; right:0; top:21px; height:1px; background:var(--line) }
      .rail .tick { position:absolute; top:13px; width:1px; height:17px; background:var(--muted); opacity:.55 }
      .rail .tick:hover { opacity:1; background:var(--accent); width:2px }
      .rail .tick.act { top:5px; height:33px; width:2px; background:var(--accent); opacity:1 }
      .rail .yr { position:absolute; top:30px; font-family:var(--mono); font-size:11px; color:var(--muted) }
      .railcap { margin:2px 0 18px } .nowmark { color:var(--accent) }
      .lawbody { font-family:var(--serif); font-size:16.5px; line-height:1.62 }
      /* Phones: law texts carry wide tables and long pre blocks. Let each scroll inside
         itself rather than pushing the whole page sideways. Desktop is untouched. */
      @media (max-width:640px) {
        main { padding:20px 14px 48px }
        table { display:block; overflow-x:auto }
        pre { overflow-x:auto }
        header { gap:12px; padding:12px 14px }
        h1 { font-size:25px }
        .rail .yr { font-size:10px }
      }
    </style></head>
    <body>
    <header>
      <a class="brand" href="/">Lex</a>
      <a class="navlink{{(nav == "ask" ? " on" : "")}}" href="/">Ask</a>
      <a class="navlink{{(nav == "find" ? " on" : "")}}" href="/find">Find a law</a>
      <a class="navlink{{(nav == "how" ? " on" : "")}}" href="/how-it-works">How it works</a>
      <span style="flex:1"></span>
      <a class="navlink{{(nav == "dev" ? " on" : "")}}" href="/developers">for developers</a>
      <a class="navlink{{(nav == "built" ? " on" : "")}}" href="/built">how it was built</a>
    </header>
    <main>
    <h1>{{title}}</h1>
    {{(subtitle is null ? "" : $"<p class=\"sub\">{subtitle}</p>")}}
    {{body}}
    </main>
    <footer>
    <nav class="sitemap" aria-label="All pages">
      <a href="/">Ask</a> · <a href="/find">Find a law</a> · <a href="/changed">What changed</a> ·
      <a href="/stories">Stories</a> · <a href="/browse">Browse</a> · <a href="/search">Search</a> ·
      <a href="/in-force-on">In force on a date</a> · <a href="/how-it-works">How it works</a> ·
      <a href="/architecture">Architecture</a> · <a href="/built">How it was built</a> ·
      <a href="/coverage">Coverage</a> · <a href="/verify">Verify it yourself</a> ·
      <a href="/developers">Developers &amp; API</a> ·
      <a href="https://github.com/SFHAJJI/lex" rel="noopener">Source</a>
    </nav>
      LU data: Legilux — Ministère d'État, Service central de législation, Grand-Duché de Luxembourg
      (CC-BY 4.0, metadata and content files; consolidated texts reproduced verbatim from the official filestore).
      EU data: © European Union, reuse with attribution (Commission Decision 2011/833/EU);
      <b>consolidated texts have no legal effect</b> — only acts published in the Official Journal are authentic.
      Lex answers <i>what the rule was</i>, never what it means — no interpretation, no advice.
      · <a href="https://github.com/SFHAJJI/lex">source</a>
    </footer>
    </body></html>
    """;

string EnvelopeCard(LexIndexReader r, bool provisional) => $"""
    <div class="card"><table class="kv">
    <tr><td>tier</td><td>{H(r.Stamp.GetValueOrDefault("tier"))} — publisher-supplied validity dates</td></tr>
    <tr><td>history begins</td><td>{H(r.Stamp.GetValueOrDefault("history_begins"))}</td></tr>
    <tr><td>index built</td><td class="mono">{H(r.Stamp.GetValueOrDefault("built_at"))} · corpus {H(r.Stamp.GetValueOrDefault("corpus_commit"))}</td></tr>
    <tr><td>stamp signature</td><td>{(r.SignatureValid ? "<span class=\"badge ok\">valid (ECDSA-P256)</span>" : "<span class=\"badge warn\">unsigned</span>")}</td></tr>
    {(provisional ? "<tr><td>provisional</td><td><span class=\"badge warn\">future-dated: a prediction from currently enacted text, revisable by any intervening amendment</span></td></tr>" : "")}
    </table></div>
    """;

string TextWithheldBox(DocRow d) => $"""
    <div class="notice"><b>Text withheld.</b> This deployment runs in metadata-only mode: the legal text is not
    stored or republished here pending publisher rights confirmation (status <span class="mono">text_withheld</span>).
    Read the official text at
    <a href="{H(d.SourceUri)}" rel="noopener">{H(d.SourceUri)}</a>.</div>
    """;

bool IsProvisional(LexIndexReader r, DateOnly d)
{
    var builtAt = r.Stamp.GetValueOrDefault("built_at", "");
    return builtAt.Length >= 10 && DateOnly.TryParse(builtAt[..10], out var b) && d > b;
}

LexIndexReader? Reader(string publisher) => readers.GetValueOrDefault(publisher);

string DocTitle(DocRow d) => d.TitleShort ?? d.Title ?? d.GroupKey;

// Legilux titles are prefixed "Version consolidée applicable au DD/MM/YYYY : <real title>".
// In a list of changes that prefix is noise — the date is already its own column.
string? TitleShorten(string? t)
{
    if (string.IsNullOrEmpty(t)) return t;
    var i = t.IndexOf(" : ", StringComparison.Ordinal);
    if (i > 0 && t.StartsWith("Version consolidée", StringComparison.OrdinalIgnoreCase)) t = t[(i + 3)..];
    else if (i > 0 && t.StartsWith("Konsolidierte", StringComparison.OrdinalIgnoreCase)) t = t[(i + 3)..];
    return t.Length > 110 ? t[..110].TrimEnd() + "…" : t;
}

string Interval(DocRow d) => d.ValidTo is null ? $"{d.ValidFrom} → <i>open</i>" : $"{d.ValidFrom} → {d.ValidTo}";

string RenderDiff(string oldText, string newText)
{
    var oldLines = oldText.Split('\n');
    var newLines = newText.Split('\n');

    // Consolidations share most content: trim the common prefix/suffix, then LCS the middle.
    int prefix = 0;
    while (prefix < oldLines.Length && prefix < newLines.Length && oldLines[prefix] == newLines[prefix]) prefix++;
    int suffix = 0;
    while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix
           && oldLines[^(suffix + 1)] == newLines[^(suffix + 1)]) suffix++;

    var o = oldLines.AsSpan(prefix, oldLines.Length - prefix - suffix).ToArray();
    var n = newLines.AsSpan(prefix, newLines.Length - prefix - suffix).ToArray();

    var sb = new StringBuilder();
    sb.Append($"<p class=\"sub\">{o.Length:n0} line(s) in the old middle, {n.Length:n0} in the new; {prefix:n0} unchanged leading and {suffix:n0} trailing lines trimmed.</p>");

    if ((long)o.Length * n.Length > 2_250_000) // DP cap ≈ 1500×1500
    {
        var oldSet = o.ToHashSet(StringComparer.Ordinal);
        var newSet = n.ToHashSet(StringComparer.Ordinal);
        var removed = o.Where(l => !newSet.Contains(l)).Take(150).ToList();
        var added = n.Where(l => !oldSet.Contains(l)).Take(150).ToList();
        sb.Append("<div class=\"notice\">Change too large for an exact line diff here — showing removed/added line samples; exact comparison at the official source links above.</div>");
        sb.Append("<div class=\"card\"><pre style=\"white-space:pre-wrap;font-size:13px;margin:0\">");
        foreach (var l in removed) sb.Append($"<span style=\"color:var(--accent)\">− {H(Trunc(l))}</span>\n");
        foreach (var l in added) sb.Append($"<span style=\"color:var(--ok)\">+ {H(Trunc(l))}</span>\n");
        sb.Append("</pre></div>");
        return sb.ToString();
    }

    // Classic LCS DP on the trimmed middle.
    var dp = new int[o.Length + 1, n.Length + 1];
    for (var i = o.Length - 1; i >= 0; i--)
        for (var j = n.Length - 1; j >= 0; j--)
            dp[i, j] = o[i] == n[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

    sb.Append("<div class=\"card\"><pre style=\"white-space:pre-wrap;font-size:13px;margin:0\">");
    int x = 0, y = 0, emitted = 0;
    const int maxEmit = 500;
    while ((x < o.Length || y < n.Length) && emitted < maxEmit)
    {
        if (x < o.Length && y < n.Length && o[x] == n[y]) { x++; y++; continue; }
        if (y < n.Length && (x >= o.Length || dp[x, y + 1] >= dp[x + 1, y]))
        { sb.Append($"<span style=\"color:var(--ok)\">+ {H(Trunc(n[y]))}</span>\n"); y++; emitted++; }
        else
        { sb.Append($"<span style=\"color:var(--accent)\">− {H(Trunc(o[x]))}</span>\n"); x++; emitted++; }
    }
    if (emitted >= maxEmit) sb.Append("<span class=\"sub\">… diff truncated at 500 changed lines …</span>\n");
    if (emitted == 0) sb.Append("<span class=\"sub\">(only whitespace-level differences in the extraction)</span>\n");
    sb.Append("</pre></div>");
    return sb.ToString();

    static string Trunc(string s) => s.Length > 300 ? s[..300] + "…" : s;
}

// ------------------------------------------------- routes

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

app.MapGet("/ai", (HttpRequest req) =>
{
    var baseUrl = $"{req.Scheme}://{req.Host}";
    var body = $$"""
        <p>Lex is <b>MCP-native</b>: you bring your AI, Lex brings the evidence. Your model asks the
        nine Lex tools for the law as it stood on a date, and composes its answer from returned
        text, dates and hashes — Lex itself never interprets anything.</p>

        <h2>Connect in one line</h2>
        <div class="card"><b>Claude Code</b><pre class="mono" style="white-space:pre-wrap">claude mcp add --transport http lex {{baseUrl}}/mcp</pre></div>
        <div class="card"><b>Claude Desktop / any MCP client</b> — add a remote MCP server:
        <pre class="mono" style="white-space:pre-wrap">{ "mcpServers": { "lex": { "url": "{{baseUrl}}/mcp" } } }</pre></div>

        <h2>What a conversation looks like</h2>
        <div class="card"><pre style="white-space:pre-wrap;font-size:14px;margin:0">
            You     What did Luxembourg data-protection law require about breach
                    notification in March 2019?

            AI  →   search("notification violation données", as_of: 2019-03-15)
                ←   loi du 1er août 2018 (lex_id lu-legilux:loi-2018-08-01-a686…), recueil protection des données
            AI  →   as_of("lu-legilux:recueil-protection_donnees", "2019-03-15")
                ←   the exact text valid that day + interval + sha256 + signed stamp

            AI      "As of 15 March 2019, the applicable framework was … [quotes the
                    retrieved text; cites validity 2018-08-20 → 2019-… ; links provenance]"
        </pre></div>

        <h2>The nine tools</h2>
        <p class="sub">as_of (full/outline/select) · timeline · in_force_on · diff · search ·
        provenance · article_history · coverage —
        read-only, deterministic, every response carries its dates, its hash, and an honest refusal
        (<span class="mono">no_version_for_date</span>, <span class="mono">text_withheld</span>) when Lex cannot know.</p>

        <h2>Azure AI Foundry agents</h2>
        <div class="card">Foundry's Agent Service speaks remote MCP natively — point an agent at this
        endpoint and it gets all nine tools (no key needed; leave approvals on for writes-free comfort):
        <pre class="mono" style="white-space:pre-wrap">{ "type": "mcp", "server_label": "lex", "server_url": "{{baseUrl}}/mcp", "require_approval": "never" }</pre></div>

        <p>Want to try it without installing anything? The capped <a href="/ask">built-in playground</a>
        runs the same tools. Prefer no AI at all? Everything is also a <a href="/">permalink</a>.</p>
        """;
    return Results.Content(Page("Use Lex with your AI", body,
        "your model + our evidence — MCP endpoint, one line to connect"), "text/html");
});

// ---- /ask playground: chat over the MCP tools, grounded and capped ----
app.MapGet("/ask", () => Results.Redirect("/"));

app.MapGet("/", () =>
{
    // stat strip computed from the mounted indexes at render time — never hand-written numbers
    var cov = readers.Values.Select(r => r.Coverage()).ToList();
    var strip = $"""
        <p class="sub" style="margin:2px 0 14px">
        <span class="badge">{cov.Sum(c => c.Groups):n0} works</span>
        <span class="badge">{cov.Sum(c => c.Rows):n0} versions</span>
        <span class="badge">{cov.Sum(c => c.TextServed):n0} with full text</span>
        <span class="badge">{H(cov.Select(c => c.EarliestValidFrom).Min())} → {H(cov.Select(c => c.LatestValidFrom).Max())}</span>
        <span class="badge ok">signed indexes</span>
        <span class="badge">9 MCP tools</span>
        <span class="badge">Luxembourg + EU</span></p>
        """;
    // Story cards: same source as /stories, computed live so a figure can never drift into fiction.
    string StoryCard(string publisher, string work, string title, string sub)
    {
        if (!readers.TryGetValue(publisher, out var rr)) return "";
        var vs = rr.Timeline(work).GroupBy(v => v.ValidFrom, StringComparer.Ordinal).Select(g => g.First())
                   .OrderBy(v => v.ValidFrom, StringComparer.Ordinal).ToList();
        if (vs.Count == 0) return "";
        var ds = vs.Select(v => DateOnly.TryParse(v.ValidFrom, out var d) ? d : (DateOnly?)null)
                   .Where(d => d.HasValue).Select(d => d!.Value).ToList();
        var gaps = ds.Zip(ds.Skip(1), (a, b) => b.DayNumber - a.DayNumber).OrderBy(g => g).ToList();
        var med = gaps.Count > 0 ? gaps[gaps.Count / 2] : 0;
        return $"""
            <a class="story" href="/{H(publisher)}/{H(work)}">
              <b>{H(title)}</b><em>{H(sub)}</em>
              <span class="big">{vs.Count} versions</span>
              <em>{(med > 0 ? $"rewritten every {med} days" : "")}</em></a>
            """;
    }

    var body = """
        <p class="lede">Ask in plain language. Get the exact text that applied that day —
        and the official source it came from.</p>

        <!-- The workspace mounts here. Without JavaScript the page still explains itself
             and every permalink below still works — those are server-rendered documents. -->
        <div id="workspace"><noscript><p class="sub">The interactive workspace needs JavaScript.
          Everything is also reachable as plain pages: <a href="/find">find a law</a>,
          <a href="/changed">what changed</a>, <a href="/stories">stories</a>.</p></noscript></div>
        """
        + $"""
        <div class="frontdoor">
        <div class="storyrow">
          {StoryCard("lu-legilux", "loi-2020-07-17-a624", "The law that could not sit still", "Covid measures act, 2020–2024")}
          {StoryCard("lu-legilux", "constitution-1868-10-17-n1", "A constitution, revised in public", "Luxembourg, 1919 → the 2023 reform")}
          <a class="story" href="/lu-legilux/rgd-1998-08-03-n4/1900-01-01">
            <b>Ask for 1900. Watch it refuse.</b><em>the honest half of the demo</em>
            <span class="big">no guess</span><em>it says what it does not have</em></a>
        </div>

        <h2>What this is</h2>
        <p>A law is not one document — it is a different document on every date it was amended.
        Lex holds {cov.Sum(c => c.Groups):n0} Luxembourg and EU laws as {cov.Sum(c => c.Rows):n0} dated
        snapshots, exactly as the official publishers issued them, and answers the only question that
        matters in practice: <b>what did the rule say on that day?</b></p>
        <p class="sub"><b>Not legal advice, and not the official text.</b> Consolidated versions have no
        legal force — only the official gazette does. Lex reports what a text said; never what it means
        for your situation. Every answer shows the source it came from, so you never have to take its
        word for anything. <a href="/how-it-works">How it works →</a></p>

        <p class="sub" style="margin-top:22px">
        <span class="badge">{cov.Sum(c => c.Groups):n0} laws</span>
        <span class="badge">{cov.Sum(c => c.Rows):n0} dated versions</span>
        <span class="badge">{cov.Sum(c => c.TextServed):n0} with full text</span>
        <span class="badge">{H(cov.Select(c => c.EarliestValidFrom).Min())} → {H(cov.Select(c => c.LatestValidFrom).Max())}</span>
        <span class="badge ok">cryptographically signed</span>
        <span class="badge">updated nightly</span></p>
        <p class="sub">Free public assistant with a daily limit.
        <a href="/developers">Connect your own AI</a> for unlimited use.</p>
        </div>
        """
        // Story cards keep their styles; the workspace ships its own stylesheet.
        + """
        <style>
          .lede { font-size:18px; color:var(--muted); margin:0 0 18px; max-width:62ch }
          /* Once a law, a period or a search is loaded, the front-door content is noise.
             The workspace sets data-workspace on <body>; everything promotional steps aside. */
          body[data-workspace="active"] .frontdoor { display:none }
          .storyrow { display:grid; grid-template-columns:repeat(auto-fit,minmax(210px,1fr)); gap:14px; margin:30px 0 10px }
          a.story { display:block; border:1px solid var(--line); border-radius:10px; padding:15px; color:inherit; background:var(--card) }
          a.story:hover { border-color:var(--accent); text-decoration:none }
          a.story b { font-family:var(--serif); font-size:16px; display:block; margin-bottom:3px }
          a.story em { font-style:normal; font-size:13px; color:var(--muted); display:block }
          a.story .big { font-family:var(--serif); font-size:25px; color:var(--accent); display:block;
                         margin:9px 0 2px; line-height:1; font-variant-numeric:tabular-nums }
        </style>
        <link rel="stylesheet" href="/app/workspace.css">
        <script type="module" src="/app/workspace.js"></script>
        """;
    return Results.Content(Page("Luxembourg and EU law, as it stood on any date.", body, null, "ask"), "text/html");
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

app.MapGet("/architecture", () =>
{
    var body = """
        <p>Lex answers one question — <b>what did the rule say on that date?</b> — for Luxembourg and EU law,
        in a way a developer can build on and an auditor can check. Everything below is open source and open data.</p>

        <h2>Two layers, one hash chain</h2>
        <div class="card"><pre class="mono" style="white-space:pre-wrap;font-size:12.5px;margin:0">EVIDENCE LAYER (append-only, verbatim)          CONSUMPTION LAYER (regenerable, clean)
        lex-corpus-lu-legilux   lex-corpus-eu-eurlex   lex-articles
        the exact bytes the state published       →   per-ARTICLE Markdown + JSON
        sha256 per file, observation chains            stable publisher-minted anchors
                                                       validity intervals per provision
                     deterministic, versioned,          per-anchor history + renumbering events
                     IMMUTABLE extraction profiles          │
                     (akn-lu/1, xhtml-eu/1 — code,          ▼
                      never an LLM)                    signed SQLite indexes (lex-index/2)
                                                       provisions + FTS + time axis, ECDSA-P256 stamp
                                                            │
                            this site · /mcp (9 tools, any MCP client) · datasets</pre></div>

        <p>Every provision's <span class="mono">text_sha256</span> chains to a verbatim-file sha256 in the evidence
        repo: re-run the pinned open-source extractor on the state's bytes and you get these bytes.
        <a href="/verify">Verify it yourself</a> — the defence is never "trust Lex".</p>

        <h2>The retrieval unit is the article</h2>
        <p>Search hits, <span class="mono">as_of</span> (with <span class="mono">outline</span> and
        <span class="mono">select</span> modes), and the <span class="mono">article_history</span> tool all operate
        per provision. "What did Article 92 say over its life?" is a file read: every distinct text as a validity
        interval, plus mechanically detected renumberings (identical-hash matching — never interpretation).</p>

        <h2>Honesty as an API contract</h2>
        <div class="card"><table>
        <tr><th>refusal status</th><th>meaning</th></tr>
        <tr><td class="mono">no_version_for_date</td><td>the work exists; no version was valid on that date</td></tr>
        <tr><td class="mono">unknown_work / unknown_anchor</td><td>Lex does not hold it — and says so</td></tr>
        <tr><td class="mono">anchor_not_in_version</td><td>that article did not exist in that version (knowing this IS the product)</td></tr>
        <tr><td class="mono">text_withheld</td><td>metadata held, text gate not cleared; official link provided</td></tr>
        <tr><td class="mono">outside_observed_window</td><td>before the observation history begins</td></tr>
        </table></div>
        <p>A flagged wrong answer is still wrong, so Lex refuses instead; <a href="/coverage">coverage</a> exists to
        state what we do <b>not</b> have. The AI layer (<a href="/">the front page</a>) is additive and separated:
        one model + system prompt over the same in-process tool core the public <span class="mono">/mcp</span> serves
        — parity by construction; no framework, no interpretation (fitness rule F10).</p>

        <h2>Build on it</h2>
        <p>
        <a href="https://github.com/SFHAJJI/lex-articles">lex-articles</a> — machine-readable corpus (CC-BY, SCHEMA.md contract) ·
        <a href="https://github.com/SFHAJJI/lex">lex</a> — all code, Apache-2.0, incl. the
        <a href="https://github.com/SFHAJJI/lex/blob/main/docs/lex-spec-v4.md">full decision record (D1–D47)</a> ·
        <a href="https://github.com/SFHAJJI/lex-corpus-lu-legilux">evidence repos</a> ·
        hosted MCP: <span class="mono">claude mcp add --transport http lex https://law.soufien.lu/mcp</span></p>
        """;
    return Results.Content(Page("Architecture", body,
        "the evidence layer, the article layer, the signed indexes — and why you don't have to trust us"), "text/html");
});

// ---- auditor surface: public key, live attestation, verify-it-yourself ----
app.MapGet("/pubkey.pem", () =>
{
    var pem = readers.Values.Select(r => r.Stamp.GetValueOrDefault("public_key")).FirstOrDefault(p => !string.IsNullOrEmpty(p));
    return pem is null ? Results.NotFound() : Results.Text(pem, "application/x-pem-file");
});

app.MapGet("/attestation.json", () =>
{
    var collections = new JsonArray();
    foreach (var r in readers.Values)
    {
        var stampObj = new JsonObject();
        foreach (var (k, v) in r.Stamp.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            stampObj[k] = v;
        collections.Add(new JsonObject
        {
            ["collection"] = r.Collection,
            ["signature_valid_at_load"] = r.SignatureValid,
            ["stamp"] = stampObj,
        });
    }
    return Results.Content(new JsonObject
    {
        ["what"] = "attestation of currency: the complete signed stamp of every index this deployment serves",
        ["signature_binds"] = "the canonical stamp text: every stamp field except signature/public_key, sorted by key, joined as k=v lines",
        ["signature_format"] = "ECDSA-P256-SHA256, IEEE P1363 (r||s, 64 bytes), base64",
        ["verify"] = "see /verify",
        ["served_at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        ["collections"] = collections,
    }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), "application/json");
});

app.MapGet("/verify", () =>
{
    var body = $$"""
        <p>Every index this site serves carries a <b>signed stamp</b>. The signature binds: schema version,
        corpus commit, build time, attribution, the full NOTICE text, and the corpus statistics — the
        canonical text is every stamp field except <span class="mono">signature</span>/<span class="mono">public_key</span>,
        sorted by key, joined as <span class="mono">k=v</span> lines. Algorithm:
        <span class="mono">ECDSA-P256-SHA256</span>, signature format IEEE P1363 (r||s, 64 bytes), base64.</p>

        <h2>What it does — and does not — attest</h2>
        <p>It attests that this exact index (schema, corpus commit, build) was produced by the holder of the
        Lex signing key. It does <b>not</b> attest that the underlying text matches the publisher — that is what
        the hash chain is for: every provision's <span class="mono">text_sha256</span> derives deterministically from a
        verbatim publisher file whose sha256 is recorded in the open corpus repos. Re-run the pinned open-source
        extractor on the state's bytes and you get these bytes — the defence is never "trust Lex".</p>

        <h2>Verify the stamp yourself</h2>
        <div class="card"><pre class="mono" style="white-space:pre-wrap">curl -s https://law.soufien.lu/attestation.json -o att.json
        curl -s https://law.soufien.lu/pubkey.pem -o pubkey.pem
        python3 - &lt;&lt;'EOF'
        import json, base64
        from cryptography.hazmat.primitives.serialization import load_pem_public_key
        from cryptography.hazmat.primitives.asymmetric import ec, utils as au
        from cryptography.hazmat.primitives import hashes
        att = json.load(open('att.json'))
        pub = load_pem_public_key(open('pubkey.pem','rb').read())
        for c in att['collections']:
            s = c['stamp']
            canon = chr(10).join(f'{k}={v}' for k, v in sorted(s.items()) if k not in ('signature','public_key')).encode()
            raw = base64.b64decode(s['signature'])
            r, sv = int.from_bytes(raw[:32],'big'), int.from_bytes(raw[32:],'big')
            pub.verify(au.encode_dss_signature(r, sv), canon, ec.ECDSA(hashes.SHA256()))
            print(c['collection'], 'OK', s['corpus_commit'], s['built_at'])
        EOF</pre></div>

        <h2>Verify a citation against the state's bytes</h2>
        <p>Clone the evidence repo and the code, then re-derive offline:</p>
        <div class="card"><pre class="mono" style="white-space:pre-wrap">git clone https://github.com/SFHAJJI/lex &amp;&amp; git clone https://github.com/SFHAJJI/lex-corpus-lu-legilux
        cd lex &amp;&amp; dotnet run --project src/Lex.Ingest -- verify derive --publisher lu-legilux --corpus ../lex-corpus-lu-legilux --articles ../lex-articles</pre></div>
        <p class="sub">Extraction profiles are immutable (<span class="mono">akn-lu/1</span>, <span class="mono">xhtml-eu/1</span>);
        a citation pinned under a profile verifies under that profile, forever. Contract:
        <a href="https://github.com/SFHAJJI/lex-articles/blob/main/SCHEMA.md">SCHEMA.md</a>.</p>
        """;
    return Results.Content(Page("Verify", body,
        "the signature, the hash chain, and how to check both without trusting us"), "text/html");
});

app.MapGet("/healthz", () => Results.Text("ok"));

app.MapGet("/browse", () =>
{
    var sb = new StringBuilder();
    sb.Append("""
        <p>Regulators publish the current rule; audits, investigations and disputes are about a <b>past date</b>.
        Lex keeps every version it has seen and answers <i>“what did this say on 15&nbsp;March&nbsp;2022?”</i>
        with the exact validity interval, a timeline, a hashed provenance record — and an honest refusal when it cannot know.</p>
        """);
    foreach (var r in readers.Values)
    {
        var c = r.Coverage();
        sb.Append($"""
            <div class="card">
            <b>{H(r.Stamp.GetValueOrDefault("publisher_name"))}</b>
            <span class="badge">tier {H(c.Stamp.GetValueOrDefault("tier"))}</span>
            <span class="badge">{c.Groups:n0} works</span>
            <span class="badge">{c.Rows:n0} versions</span>
            <span class="badge">{H(c.EarliestValidFrom)} → {H(c.LatestValidFrom)}</span>
            <span class="badge {(r.SignatureValid ? "ok" : "warn")}">{(r.SignatureValid ? "signed index" : "unsigned")}</span>
            <div class="sub" style="margin-top:6px">Dense and reliable from 2017 onward; real but sparse before; isolated snapshots back to 1849; forward to 2030.</div>
            </div>
            """);
    }
    sb.Append("""
        <h2>Try it</h2>
        <ul>
          <li><a href="/lu-legilux/rgd-1998-08-03-n4/2018-01-01">Nouveau Code de procédure civile — as it stood on 1 Jan 2018</a></li>
          <li><a href="/lu-legilux/code-environnement">Code de l'environnement — full timeline (195 versions)</a></li>
          <li><a href="/lu-legilux/loi-2006-07-31-n2/2020-03-15">Code du travail — as it stood on 15 Mar 2020</a></li>
          <li><a href="/lu-legilux/recueil-protection_donnees">Recueil protection des données — timeline</a></li>
          <li><a href="/in-force-on?date=2022-03-15&amp;kind=CODE">Which codes were in force on 15 Mar 2022?</a></li>
          <li><a href="/eu-eurlex/32013r0575">CRR (EU) 575/2013 — 22 consolidated versions, incl. future-dated</a></li>
          <li><a href="/eu-eurlex/32016r0679/2019-01-01">GDPR as it stood on 1 Jan 2019 — with full text</a></li>
          <li><a href="/eu-eurlex/32013r0575/diff/2020-01-01/2024-01-01">CRR: what changed between 2020 and 2024?</a></li>
        </ul>
        <h2>Ask your own question</h2>
        <form class="inline" action="/search"><input name="q" placeholder="search titles, e.g. protection des données" style="flex:1;min-width:240px"><button>Search</button></form>
        <form class="inline" action="/go-asof">
          <input name="work" placeholder="work slug, e.g. code-travail" style="flex:1;min-width:200px">
          <input name="date" type="date" value="2022-03-15">
          <button>As of date</button>
        </form>
        """);
    return Results.Content(Page("Browse the corpus",
        sb.ToString(), "Luxembourg + EU. Every answer carries its dates, its source and its hash — never an interpretation."), "text/html");
});

app.MapGet("/go-asof", (string work, string date) =>
    Results.Redirect($"/lu-legilux/{Uri.EscapeDataString(work.Trim())}/{Uri.EscapeDataString(date)}"));

app.MapGet("/coverage", () =>
{
    var sb = new StringBuilder();
    sb.Append("""
        <p>This tool exists to say what we do <b>not</b> have. A system that cannot state its own gaps
        cannot be trusted with a completeness question.</p>
        """);
    foreach (var r in readers.Values)
    {
        var c = r.Coverage();
        sb.Append($"<h2>{H(r.Stamp.GetValueOrDefault("publisher_name"))} <span class=\"badge\">{H(c.Collection)}</span></h2>");
        sb.Append("<div class=\"card\"><table><tr><th>document type</th><th>versions</th></tr>");
        foreach (var k in c.Kinds)
            sb.Append($"<tr><td>{H(k.Kind ?? "(untyped)")}</td><td>{k.Versions:n0}</td></tr>");
        sb.Append($"</table></div>{EnvelopeCard(r, false)}");
        var luGap = c.Collection == "lu-legilux"
            ? """
              The publisher only maintains consolidated (amendments-merged) editions for some laws —
              the codes and frequently amended acts. Lex holds <b>all of those</b>. The other
              ≈24,579 Luxembourg acts never get a consolidated edition; they are <b>not here yet</b>
              (and we won't guess dates for texts we haven't seen).
              """
            : " Only flagship acts are ingested so far; the wider consolidated acquis is scheduled.";
        sb.Append($"""
            <div class="notice"><b>What we hold — and what we honestly don't.</b>
            {c.Groups:n0} laws in {c.Rows:n0} dated snapshots.{luGap}
            Of those snapshots, <b>{c.TextServed:n0}</b> carry the full official text;
            <b>{c.Rows - c.TextServed:n0}</b> exist as dated entries with a link but no stored text,
            because the publisher has no machine-readable file for that (usually old) version —
            those answer with <span class="mono">text_withheld</span> instead of pretending.
            History can never go deeper than what the publisher itself digitised.</div>
            """);
    }
    return Results.Content(Page("Coverage — what we hold, and what we lack", sb.ToString()), "text/html");
});

app.MapGet("/in-force-on", (string? date, string? publisher, string? kind, int? page) =>
{
    var sb = new StringBuilder();
    var kindOptions = string.Join("", (readers.Values.FirstOrDefault()?.Coverage().Kinds ?? [])
        .Where(k => k.Kind is not null)
        .Select(k => $"<option {(k.Kind == kind ? "selected" : "")}>{H(k.Kind)}</option>"));
    sb.Append($"""
        <form class="inline">
          <input type="date" name="date" value="{H(date ?? "2022-03-15")}">
          <select name="kind"><option value="">any type</option>{kindOptions}</select>
          <button>Show</button>
        </form>
        """);
    if (DateOnly.TryParse(date, out var d))
    {
        var p = Math.Max(0, (page ?? 1) - 1);
        const int limit = 50;
        foreach (var r in readers.Values.Where(r => publisher is null || r.Collection == publisher))
        {
            var (rows, total) = r.InForceOn(d, new FilterSet(null, null, string.IsNullOrEmpty(kind) ? null : kind, null), limit, p * limit);
            sb.Append($"<h2>{H(r.Stamp.GetValueOrDefault("publisher_name"))} — {total:n0} works in force on {d:yyyy-MM-dd}</h2>");
            sb.Append("<div class=\"card\"><table><tr><th>work</th><th>type</th><th>version valid</th></tr>");
            foreach (var row in rows)
                sb.Append($"""
                    <tr><td><a href="/{H(row.Collection)}/{H(row.GroupKey)}/{d:yyyy-MM-dd}">{H(DocTitle(row))}</a></td>
                    <td><span class="badge">{H(row.Kind)}</span></td><td class="mono">{Interval(row)}</td></tr>
                    """);
            sb.Append("</table></div>");
            if (total > limit)
            {
                sb.Append("<p>");
                if (p > 0) sb.Append($"<a href=\"?date={d:yyyy-MM-dd}&kind={H(kind)}&page={p}\">← previous</a> &nbsp;");
                if ((p + 1) * limit < total) sb.Append($"<a href=\"?date={d:yyyy-MM-dd}&kind={H(kind)}&page={p + 2}\">next →</a>");
                sb.Append($" <span class=\"sub\">page {p + 1} of {(total + limit - 1) / limit}</span></p>");
            }
            sb.Append($"""
                <div class="notice"><b>Population disclosure.</b> Basis: versioned works only ({r.Coverage().Groups:n0} works).
                ≈24,579 never-consolidated LU acts are not ingested (date coverage unmeasured) — see <a href="/coverage">coverage</a>.</div>
                """);
            sb.Append(EnvelopeCard(r, IsProvisional(r, d)));
        }
    }
    return Results.Content(Page("In force on a date", sb.ToString(),
        "The compliance question in one call: which instruments applied on that day?"), "text/html");
});

app.MapGet("/search", (string? q, string? kind) =>
{
    var sb = new StringBuilder();
    sb.Append($"""
        <form class="inline"><input name="q" value="{H(q)}" placeholder="search article text &amp; titles" style="flex:1;min-width:240px"><button>Search</button></form>
        <p class="sub">Article-level full-text search over every held provision. Filters run before ranking — always.</p>
        """);
    if (!string.IsNullOrWhiteSpace(q))
    {
        foreach (var r in readers.Values)
        {
            var hits = r.Search(q, new FilterSet(null, null, string.IsNullOrEmpty(kind) ? null : kind, null), 90)
                .GroupBy(h => (h.Doc.GroupKey, h.Prov.Anchor)).Select(g => g.First())
                .GroupBy(h => h.Doc.GroupKey).SelectMany(g => g.Take(2))
                .Take(15)
                .ToList();
            sb.Append($"<h2>{H(r.Stamp.GetValueOrDefault("publisher_name"))} — {hits.Count} hit(s)</h2>");
            foreach (var (docRow, prov, snippet) in hits)
                sb.Append($"""
                    <div class="card"><a href="/{H(docRow.Collection)}/{H(docRow.GroupKey)}/{H(docRow.ValidFrom)}#{H(prov.Anchor)}"><b>{H(DocTitle(docRow))}</b>
                    — {H(prov.Num ?? prov.Heading ?? prov.Anchor)}</a>
                    <span class="badge">{H(docRow.Kind)}</span> <span class="badge mono">{Interval(docRow)}</span>
                    <div class="snippet">{H(snippet)}</div>
                    <div class="mono sub">{H(prov.ProvisionId)}</div></div>
                    """);
        }
    }
    return Results.Content(Page("Search", sb.ToString()), "text/html");
});

// ---- /built: the engineering story. Written for someone deciding whether the person who
// built this can build things — decisions, tradeoffs, failures, and how correctness is proven.
app.MapGet("/built", () =>
{
    var cov = readers.Values.Select(r => r.Coverage()).ToList();
    var body = $"""
        <p class="lede">A point-in-time legal database, an MCP server, a grounded assistant and a
        nightly pipeline — built solo. This page is the part usually left out: the decisions, the
        things that broke, and how correctness is actually proven rather than asserted.</p>

        <div class="notice"><b>Built with AI assistance.</b> The architecture, the decisions and the
        verification are mine; a great deal of the code was written with an AI pair. That is stated
        here because it is true, and because the parts that matter — the decision record, the failure
        modes below, and the tests that catch them — are where the engineering actually lives.</div>

        <h2>The problem</h2>
        <p>Ask any legal site what a law says and you get today's text. Almost every question that
        matters is about a <b>date</b>: what applied when the contract was signed, when the fine was
        issued, when the breach happened. Official publishers do hold dated consolidated editions —
        but scattered across formats (Akoma Ntoso XML, Formex XML, legacy XHTML), with no article-level
        access and no machine interface. Lex turns that into one queryable, verifiable history.</p>

        <h2>The shape of it</h2>
        <div class="card"><pre class="mono" style="white-space:pre-wrap;margin:0;font-size:13px">
        official publishers          nightly, one scheduled job
        Legilux · EUR-Lex/Cellar     ──────────────────────────────────
                │                    ingest → anomaly gate → derive →
                │  verbatim bytes      (&gt;5% drop =        determinism
                ▼  + sha256             commit nothing)     guard
          EVIDENCE REPOS ──────────────────────────────────────────►
          append-only, never rewritten            │
                │                                 ▼
                │  deterministic extraction   DERIVED REPO (per article)
                ▼  (immutable profiles)           │
          SIGNED SQLITE INDEX (ECDSA P-256) ◄─────┘
                │
                ├──► MCP server  (9 tools, public, no key)
                └──► this site   (Container Apps, scale-to-zero)</pre></div>
        <p class="sub">Azure: Container Apps behind a managed certificate, Container Registry, Azure
        OpenAI (gpt-5-mini) for the assistant, Application Insights via OpenTelemetry, Azure DNS.
        The web app runs at 0.25 vCPU and <b>scales to zero</b> — idle cost is essentially the
        registry and the DNS zone.</p>

        <h2>Decisions worth defending</h2>
        <div class="card"><table>
        <tr><th>decision</th><th>why, and what it cost</th></tr>
        <tr><td><b>Store publisher bytes verbatim</b></td>
            <td>The evidence layer is never "cleaned". A hash over cleaned text proves nothing about
            what the state published. Cost: two layers to maintain instead of one.</td></tr>
        <tr><td><b>Extraction profiles are immutable</b></td>
            <td>Once <span class="mono">akn-lu/1</span> is published, its output for a given input can
            never change — a frozen-fingerprint test fails the build if it does. Improvements ship as
            a new profile. Cost: no silent fixes, ever.</td></tr>
        <tr><td><b>Refusals are part of the API</b></td>
            <td>Seven typed refusal codes instead of empty results. A caller can distinguish "no such
            law", "no version that day" and "text withheld". Cost: more surface to test.</td></tr>
        <tr><td><b>A small model, tightly fenced</b></td>
            <td>The assistant only picks lookups and quotes results — no agent framework, no chain of
            reasoning over law. Cheap, auditable, and wrong answers are visible against the evidence
            shown beside them.</td></tr>
        <tr><td><b>Nightly commits nothing when unsure</b></td>
            <td>A &gt;5% drop in works, or a re-derivation that is not byte-identical, aborts the run.
            A partial upstream response must never rewrite history.</td></tr>
        </table>
        <p class="sub" style="margin:8px 0 0">Forty-eight numbered decisions like these are recorded in
        the specification, each with its rationale — so "why did you do it that way" has a written
        answer rather than a recollection.</p></div>

        <h2>The machinery that keeps it fresh</h2>
        <p>Law changes while you sleep, so the corpus is rebuilt while I do. One scheduled job at
        02:17 UTC drives the whole fleet — no manual step exists, and there is deliberately only one
        credential and one cron for all publishers.</p>
        <div class="card"><table>
        <tr><th>stage</th><th>what it does, and how it refuses to do damage</th></tr>
        <tr><td><b>1. Ingest</b></td><td>Asks each publisher what versions exist, downloads any it has
        not seen, and writes them <i>verbatim</i>. Existing files are never reopened for writing —
        the evidence layer is append-only by construction.</td></tr>
        <tr><td><b>2. Anomaly gate</b></td><td>If the work count drops more than 5%, the run assumes the
        upstream response was partial, discards everything and commits nothing. A bad night leaves
        yesterday's good data in place.</td></tr>
        <tr><td><b>3. Derive</b></td><td>Regenerates the per-article layer from the verbatim files.</td></tr>
        <tr><td><b>4. Determinism guard</b></td><td>If derived output changed while no source file did,
        that means the extractor is non-deterministic — the run fails loudly and commits nothing,
        because a silent extraction drift would corrupt history.</td></tr>
        <tr><td><b>5. Index &amp; publish</b></td><td>Rebuilds the search index, signs it (ECDSA P-256),
        publishes it as a release asset, regenerates the JSONL and Parquet datasets.</td></tr>
        <tr><td><b>6. Report</b></td><td>Writes a three-state outcome per publisher
        (<span class="mono">ran_committed</span> / <span class="mono">ran_no_change</span> /
        <span class="mono">failed_*</span>) and opens an issue on failure.</td></tr>
        </table></div>
        <p>The result travels with the data: every index carries a signed stamp recording when it was
        built and from which corpus commit, and every tool response returns it. <b>This is that stamp,
        read live from the running indexes:</b></p>
        <div class="card"><table>
        <tr><th>publisher</th><th>index built</th><th>from corpus commit</th><th>signature</th></tr>
        {string.Join("", readers.Values.OrderBy(r => r.Collection, StringComparer.Ordinal).Select(r => $"""
            <tr><td>{H(r.Collection)}</td>
            <td class="mono">{H(r.Stamp.GetValueOrDefault("built_at"))}</td>
            <td class="mono">{H(r.Stamp.GetValueOrDefault("corpus_commit"))}</td>
            <td>{(r.SignatureValid ? "<span class=\"badge ok\">valid</span>" : "<span class=\"badge warn\">unsigned</span>")}</td></tr>
            """))}
        </table>
        <p class="sub" style="margin:8px 0 0">Nothing here is typed by hand — if the pipeline stopped,
        this table would say so. The same values come back from the
        <a href="/developers">coverage tool</a> in every API response.</p></div>

        <h2>What broke, and what it taught</h2>
        <div class="card">
        <p><b>A silently dead search, caught in production.</b> The nightly job built the search index
        without the per-article layer. Nothing errored: the index was valid, signed, and published —
        it just had zero provisions in it, so search returned nothing. The automated eval suite caught
        it by asking a question a user would ask and noticing the assistant could no longer find a
        Luxembourg code.</p>
        <p class="sub">Fix: the index step now runs after derivation and takes the article layer as a
        required input, so the failure cannot recur. Lesson: a green build is not a working system —
        the only tests that would have caught this are the ones that exercise it end to end, the way
        someone actually uses it.</p>
        <p><b>A parser that quietly duplicated text.</b> Adding Formex XML support introduced doubled
        paragraphs where an article had introductory text followed by a list. It looked plausible on
        screen. It was caught by re-reading real output rather than trusting a passing test, fixed the
        same day, and pinned with a fingerprint test so the profile can never drift again.</p>
        </div>

        <h2>How correctness is proven, not claimed</h2>
        <div class="card"><table>
        <tr><th>mechanism</th><th>what it guarantees</th></tr>
        <tr><td>34 unit tests</td><td>parsers, temporal logic, index schema, signing</td></tr>
        <tr><td>Frozen profile fingerprints</td><td>a published extraction can never change output</td></tr>
        <tr><td>Determinism guard in CI</td><td>re-derivation is byte-identical or the run commits nothing</td></tr>
        <tr><td>10 end-to-end AI evals</td><td>the assistant picks the right tools and never cites a source it was not given</td></tr>
        <tr><td>LLM-judged groundedness</td><td>answers scored against the evidence actually returned</td></tr>
        <tr><td>ECDSA-signed indexes</td><td>anyone can verify a build was not altered — <a href="/verify">recipe</a></td></tr>
        </table></div>

        <h2>Scale</h2>
        <p><span class="badge">{cov.Sum(c => c.Groups):n0} laws</span>
        <span class="badge">{cov.Sum(c => c.Rows):n0} dated versions</span>
        <span class="badge">333,000+ articles indexed</span>
        <span class="badge">~7,200 lines of C# across 10 projects</span>
        <span class="badge">3 XML/HTML dialects parsed</span>
        <span class="badge">nightly, unattended</span></p>

        <h2>What I would do differently</h2>
        <p>Version the index schema migration path from day one — schema v2 required a full rebuild
        rather than a migration. Put the end-to-end evals in the nightly pipeline, not only in my hands;
        they caught the worst bug of the project and should be a gate, not a habit. And treat the
        derived layer's release assets as part of the deploy, not a follow-up step — the two times
        something shipped stale, that was why.</p>

        <p class="sub"><a href="/developers"><b>Use it from your own code →</b></a> ·
        <a href="/architecture"><b>The data model →</b></a> ·
        <a href="/verify"><b>Verify a build yourself →</b></a> ·
        <a href="https://github.com/SFHAJJI/lex" rel="noopener"><b>Source →</b></a></p>
        """;
    return Results.Content(Page("How it was built", body, null, "how"), "text/html");
});

// ---- /developers: everything an engineer needs — the nine tools, four ways to connect,
// the datasets, the repos. /ai kept as an alias so older links survive.
app.MapGet("/developers", (HttpRequest req) =>
{
    var baseUrl = $"{req.Scheme}://{req.Host}";
    var cov = readers.Values.Select(r => r.Coverage()).ToList();
    var body = $$"""
        <p class="lede">Lex is MCP-native: you bring the model, Lex brings the evidence. Eight
        read-only tools over signed indexes — no key, no account, no rate limit on the endpoint.</p>

        <h2>Connect</h2>
        <div class="card"><b>Claude Code</b>
        <pre class="mono" style="white-space:pre-wrap;margin:6px 0 0">claude mcp add --transport http lex {{baseUrl}}/mcp</pre></div>
        <div class="card"><b>Claude Desktop, Cursor, any MCP client</b>
        <pre class="mono" style="white-space:pre-wrap;margin:6px 0 0">{ "mcpServers": { "lex": { "url": "{{baseUrl}}/mcp" } } }</pre></div>
        <div class="card"><b>Azure AI Foundry Agent Service</b> — remote MCP is native:
        <pre class="mono" style="white-space:pre-wrap;margin:6px 0 0">{ "type": "mcp", "server_label": "lex", "server_url": "{{baseUrl}}/mcp", "require_approval": "never" }</pre></div>
        <div class="card"><b>No framework at all</b> — it is JSON-RPC over one POST:
        <pre class="mono" style="white-space:pre-wrap;margin:6px 0 0">curl -X POST {{baseUrl}}/mcp -H 'Content-Type: application/json' \
          -d '{ "jsonrpc":"2.0", "id":1, "method":"tools/call",
                "params": { "name":"as_of",
                  "arguments": { "work":"eu-eurlex:32016r0679",
                    "date":"2019-03-15", "mode":"select", "anchors":"art_33" } } }'</pre></div>

        <h2>The nine tools</h2>
        <div class="card"><table>
        <tr><th>tool</th><th>arguments</th><th>answers</th></tr>
        <tr><td class="mono">as_of</td><td class="mono">work, date, [language], [mode: full\|outline\|select], [anchors]</td>
            <td>the text in force on a date. <span class="mono">outline</span> lists article anchors only;
            <span class="mono">select</span> returns just the anchors you name — use it, codes are large.</td></tr>
        <tr><td class="mono">article_history</td><td class="mono">work, anchor</td>
            <td>every distinct text one article has had, as validity intervals, plus renumbering events.</td></tr>
        <tr><td class="mono">timeline</td><td class="mono">work</td><td>all versions of a work with their validity windows.</td></tr>
        <tr><td class="mono">diff</td><td class="mono">work, from, to</td><td>what changed between two versions.</td></tr>
        <tr><td class="mono">in_force_on</td><td class="mono">date, [publisher], [document_type], [limit], [offset]</td>
            <td>everything that applied on a given day.</td></tr>
        <tr><td class="mono">search</td><td class="mono">query, [publisher], [as_of], [document_type], [language], [limit]</td>
            <td>provision-level full-text search; hits are articles, with permalinks.</td></tr>
        <tr><td class="mono">provenance</td><td class="mono">lex_id</td>
            <td>source URI, retrieval time, record hash, the append-only observation chain.</td></tr>
        <tr><td class="mono">coverage</td><td class="mono">[publisher]</td><td>what is held, and what is knowably missing.</td></tr>
        </table></div>

        <h2>Try the joysticks</h2>
        <p class="sub">This calls the same public endpoint your model would. No key, nothing installed —
        the JSON below is exactly what an MCP client receives.</p>
        <div class="card">
          <form id="pg" class="inline" style="margin:0 0 8px">
            <select id="pgtool" aria-label="Choose a tool to call">
              <option value="as_of">as_of</option>
              <option value="article_history">article_history</option>
              <option value="timeline">timeline</option>
              <option value="diff">diff</option>
              <option value="in_force_on">in_force_on</option>
              <option value="search">search</option>
              <option value="provenance">provenance</option>
              <option value="coverage">coverage</option>
              <option value="changes_in_period">changes_in_period</option>
            </select>
            <button type="submit">Call it</button>
          </form>
          <textarea id="pgargs" aria-label="Tool arguments as JSON" rows="5" style="width:100%;font-family:var(--mono);font-size:13px"></textarea>
          <pre id="pgout" class="mono" style="white-space:pre-wrap;max-height:340px;overflow:auto;font-size:12.5px;margin:10px 0 0">↑ pick a tool and press "Call it"</pre>
        </div>

        <h2>It refuses rather than guesses</h2>
        <p class="sub">Every tool returns an envelope with a status. The refusals are part of the contract:
        <span class="mono">no_version_for_date</span> · <span class="mono">unknown_work</span> ·
        <span class="mono">unknown_anchor</span> · <span class="mono">anchor_not_in_version</span> ·
        <span class="mono">no_provision_history</span> · <span class="mono">text_withheld</span> ·
        <span class="mono">outside_observed_window</span>. Build against them: an empty result and a
        refusal are different things.</p>

        <h2>Or skip the API — take the data</h2>
        <p>Every provision of every version, one row each, licence and attribution inline.
        {{cov.Sum(c => c.TextServed):n0}} versions carry full text.</p>
        <div class="card"><b>DuckDB, one line, no download:</b>
        <pre class="mono" style="white-space:pre-wrap;margin:6px 0 0">SELECT * FROM read_parquet('https://github.com/SFHAJJI/lex-articles/releases/latest/download/eu-eurlex-provisions.parquet');</pre>
        <p class="sub" style="margin:8px 0 0">Also published as <span class="mono">.jsonl.gz</span>.
        Python examples (standard library only, no pip install) live in
        <a href="https://github.com/SFHAJJI/lex-articles/tree/main/examples" rel="noopener">examples/</a> —
        load provisions, resolve a point in time, verify the hash chain, call this MCP endpoint.</p></div>

        <h2>The repositories</h2>
        <div class="card"><table>
        <tr><th>repo</th><th>what it is</th><th>licence</th></tr>
        <tr><td><a href="https://github.com/SFHAJJI/lex" rel="noopener">lex</a></td>
            <td>the engine: ingest, derive, index, MCP server, this site</td><td>Apache-2.0</td></tr>
        <tr><td><a href="https://github.com/SFHAJJI/lex-articles" rel="noopener">lex-articles</a></td>
            <td>derived layer — one Markdown+JSON record per article per version</td><td>CC-BY-4.0</td></tr>
        <tr><td><a href="https://github.com/SFHAJJI/lex-corpus-lu-legilux" rel="noopener">lex-corpus-lu-legilux</a></td>
            <td>evidence layer — Legilux's own files, verbatim, plus signed nightly index</td><td>CC-BY-4.0</td></tr>
        <tr><td><a href="https://github.com/SFHAJJI/lex-corpus-eu-eurlex" rel="noopener">lex-corpus-eu-eurlex</a></td>
            <td>evidence layer — EUR-Lex/Cellar files, verbatim</td><td>EU reuse</td></tr>
        </table>
        <p class="sub" style="margin:8px 0 0">Why four? Evidence and derivation are kept apart on purpose:
        the evidence repos are append-only and never rewritten, so a derived record can always be traced
        back to the exact bytes a state published. <a href="/architecture">The full model →</a></p></div>

        <p class="sub">Everything on this page is exercised by the project's own test and eval suites,
        and the endpoint is rebuilt nightly. <a href="/built">How it was built →</a> ·
        <a href="/verify">Verify it yourself →</a></p>
        """
        + """
        <script>
        (function () {
          const presets = {
            as_of: { work: "eu-eurlex:32016r0679", date: "2019-03-15", mode: "select", anchors: "art_33" },
            article_history: { work: "eu-eurlex:32013r0575", anchor: "art_92" },
            timeline: { work: "lu-legilux:loi-2020-07-17-a624" },
            diff: { work: "lu-legilux:loi-2020-07-17-a624", from_date: "2020-07-25", to_date: "2021-02-01" },
            in_force_on: { date: "2022-03-15", document_type: "CODE", limit: 5 },
            search: { query: "congé parental", publisher: "lu-legilux", limit: 3 },
            provenance: { lex_id: "eu-eurlex:32016r0679:2016-05-04" },
            coverage: {},
            changes_in_period: { from_date: "2020-03-01", to_date: "2021-07-01", order: "by_churn", limit: 10 }
          };
          const tool = document.getElementById('pgtool'), args = document.getElementById('pgargs'),
                out = document.getElementById('pgout'), form = document.getElementById('pg');
          function fill() { args.value = JSON.stringify(presets[tool.value], null, 2); }
          tool.addEventListener('change', fill); fill();
          form.addEventListener('submit', async function (e) {
            e.preventDefault();
            out.textContent = 'calling ' + tool.value + '…';
            let parsed;
            try { parsed = JSON.parse(args.value || '{}'); }
            catch (err) { out.textContent = 'arguments are not valid JSON: ' + err.message; return; }
            try {
              const r = await fetch('/mcp', {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'tools/call',
                                       params: { name: tool.value, arguments: parsed } })
              });
              const j = await r.json();
              const text = j.result && j.result.content && j.result.content[0]
                ? j.result.content[0].text : JSON.stringify(j, null, 2);
              let pretty; try { pretty = JSON.stringify(JSON.parse(text), null, 2); } catch { pretty = text; }
              out.textContent = pretty.length > 6000 ? pretty.slice(0, 6000) + '\n… truncated for display' : pretty;
            } catch (err) { out.textContent = 'request failed: ' + err.message; }
          });
        })();
        </script>
        """;
    return Results.Content(Page("For developers", body, null, "dev"), "text/html");
});

// ---- /how-it-works: one page, plain language first, technical depth on scroll.
// Absorbs what used to be three separate tabs (architecture, verify, coverage).
app.MapGet("/how-it-works", () =>
{
    var cov = readers.Values.Select(r => r.Coverage()).ToList();
    var body = $"""
        <p class="lede">Short version: Lex keeps every dated version of a law exactly as the official
        publisher issued it, proves it has not altered a byte, and refuses to answer when it does not
        know. Here is the longer version.</p>

        <h2>Why "what does the law say" is the wrong question</h2>
        <p>Laws are amended constantly. Ask what the Luxembourg Covid measures act said and the honest
        answer is: <i>which of its 32 texts?</i> Almost every legal question that matters — was this
        contract valid, was that fine lawful, did we comply — is really a question about a
        <b>date</b>. Most legal websites show you only today's text.</p>

        <h2>Where the text comes from</h2>
        <p>Only from the official sources: <a href="https://legilux.public.lu" rel="noopener">Legilux</a>
        for Luxembourg, <a href="https://eur-lex.europa.eu" rel="noopener">EUR-Lex</a> for the EU.
        Lex downloads the publisher's own file for each version and stores it <b>byte for byte</b>,
        untouched, with a SHA-256 fingerprint. Nothing is rewritten, summarised or "cleaned".</p>
        <p>A second layer then splits that file into one record per article — still deterministic,
        no AI involved — so you can ask for Article 92 rather than a whole 600-page regulation.
        Each article's fingerprint chains back to the publisher's original file, so any tampering
        anywhere in the chain is detectable.</p>

        <h2>How you can check all of this yourself</h2>
        <p>Every index Lex serves is cryptographically signed (ECDSA P-256). You can download the
        public key, verify the signature, recompute any article's hash, and compare it against the
        publisher's own file — all without trusting this site.
        <a href="/verify"><b>The step-by-step recipe, with code →</b></a></p>

        <h2>What it will not do</h2>
        <p>It will not guess. If you ask for a date Lex has no version for, it says so with a reason
        code (<span class="mono">no_version_for_date</span>) instead of producing a plausible text.
        The assistant on the front page is a small model whose only job is to pick the right lookup
        and quote what comes back — every answer shows the evidence underneath it, so you can check
        the answer against the source without leaving the page.
        <a href="/lu-legilux/rgd-1998-08-03-n4/1900-01-01">Watch it refuse →</a></p>

        <h2>What it holds today</h2>
        <p><span class="badge">{cov.Sum(c => c.Groups):n0} laws</span>
        <span class="badge">{cov.Sum(c => c.Rows):n0} dated versions</span>
        <span class="badge">{cov.Sum(c => c.TextServed):n0} with full text</span>
        <span class="badge">refreshed nightly</span></p>
        <p>Lex holds every Luxembourg law the state maintains a consolidated edition for, plus a set
        of EU financial and data regulations. Roughly 24,000 Luxembourg acts never receive a
        consolidated edition and are not here — and the gaps are published rather than hidden.
        <a href="/coverage"><b>Exactly what is and is not held →</b></a></p>

        <h2>Under the hood</h2>
        <p>The two-layer data model, the extraction profiles, the index schema and the refusal
        taxonomy are documented for engineers.
        <a href="/architecture"><b>The architecture page →</b></a> ·
        <a href="/built"><b>How it was built, and what was hard →</b></a> ·
        <a href="/developers"><b>Use it from your own code →</b></a></p>

        <div class="notice"><b>Not legal advice, and not the official text.</b> Consolidated versions
        have no legal force — only the version published in the official gazette does. The publishers
        say so themselves. Lex reports what a text said on a date; deciding what that means for a
        situation is a lawyer's job.</div>
        """;
    return Results.Content(Page("How it works", body, null, "how"), "text/html");
});

// ---- /changed: the corpus-wide counterpart of a diff. "What moved between two dates?"
// is the question a compliance reader actually has, and no per-work tool can answer it.
app.MapGet("/changed", (string? from, string? to, string? order, string? publisher) =>
{
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    if (!DateOnly.TryParse(to, out var toD)) toD = today;
    if (!DateOnly.TryParse(from, out var fromD)) fromD = toD.AddYears(-1);
    if (fromD > toD) (fromD, toD) = (toD, fromD);
    var byChurn = order == "by_churn";
    var f = fromD.ToString("yyyy-MM-dd");
    var t = toD.ToString("yyyy-MM-dd");

    var sb = new StringBuilder($"""
        <p class="lede">Every Luxembourg and EU law in Lex that gained a new version between two
        dates — the corpus-wide view that a single law's timeline cannot give you.</p>
        <form class="inline" method="get">
          <label class="sub">from <input type="date" name="from" value="{f}"></label>
          <label class="sub">to <input type="date" name="to" value="{t}"></label>
          <select name="order">
            <option value="by_date"{(byChurn ? "" : " selected")}>most recently changed</option>
            <option value="by_churn"{(byChurn ? " selected" : "")}>changed most often</option>
          </select>
          <button type="submit">Show changes</button>
        </form>
        <p class="sub">Quick ranges:
          <a href="/changed?from={today.AddMonths(-1):yyyy-MM-dd}&to={today:yyyy-MM-dd}">last month</a> ·
          <a href="/changed?from={today.AddYears(-1):yyyy-MM-dd}&to={today:yyyy-MM-dd}">last year</a> ·
          <a href="/changed?from=2025-01-01&to=2026-01-01">2025 → 2026</a> ·
          <a href="/changed?from=2020-03-01&to=2021-07-01&order=by_churn">the pandemic, by churn</a></p>
        """);

    var totalWorks = 0; var totalVersions = 0;
    var blocks = new StringBuilder();
    foreach (var r in readers.Values.Where(x => publisher is null || x.Collection == publisher)
                             .OrderBy(x => x.Collection, StringComparer.Ordinal))
    {
        var (works, versions) = r.ChangeTotals(f, t, null);
        totalWorks += works; totalVersions += versions;
        var rows = r.ChangesInPeriod(f, t, null, byChurn, 60);
        if (rows.Count == 0) continue;
        blocks.Append($"<h2>{H(r.Collection)} — {works:n0} law(s) moved, {versions:n0} new version(s)</h2>");
        blocks.Append("<div class=\"card\"><table><tr><th>law</th><th>new versions</th><th>window</th><th></th></tr>");
        foreach (var c in rows)
            blocks.Append($"""
                <tr><td><a href="/{H(r.Collection)}/{H(c.GroupKey)}">{H(TitleShorten(c.Title) ?? c.GroupKey)}</a>
                    <div class="sub mono" style="font-size:12px">{H(c.GroupKey)} · {c.VersionsTotal} version(s) in all</div></td>
                <td class="mono">{c.VersionsInPeriod}</td>
                <td class="mono">{H(c.FirstChange)}{(c.FirstChange == c.LastChange ? "" : " → " + H(c.LastChange))}</td>
                <td>{(c.FirstChange == c.LastChange
                        ? $"<a href=\"/{H(r.Collection)}/{H(c.GroupKey)}/{H(c.LastChange)}\">read</a>"
                        : $"<a href=\"/{H(r.Collection)}/{H(c.GroupKey)}/diff/{H(c.FirstChange)}/{H(c.LastChange)}\">what changed</a>")}</td></tr>
                """);
        blocks.Append("</table></div>");
    }

    sb.Append($"""
        <div class="card" style="border-color:var(--accent)">
          <b>{totalWorks:n0} law(s) changed</b> between {H(f)} and {H(t)},
          producing <b>{totalVersions:n0} new version(s)</b>.
          {(totalWorks == 0 ? "Nothing moved in this window — which is itself an answer." : "")}
        </div>
        """);
    sb.Append(blocks);
    sb.Append($"""
        <p class="sub">Same data, from your own code:
        <span class="mono">changes_in_period(from_date="{H(f)}", to_date="{H(t)}"{(byChurn ? ", order=\"by_churn\"" : "")})</span>
        — <a href="/developers">try it in the browser</a>.</p>
        """);
    return Results.Content(Page("What changed", sb.ToString(), null, "find"), "text/html");
});

// ---- /find: one door for the three ways of locating a law (search, browse, in-force-on).
app.MapGet("/find", () =>
{
    var body = $"""
        <p class="lede">Three ways in. All of them end at the same place: the text of a law
        as it stood on a date you choose.</p>

        <div class="card"><h2 style="margin-top:0">Search by words</h2>
        <p class="sub">Finds the individual article, not just the law — search runs over every provision.</p>
        <form class="inline" action="/search" method="get">
          <input name="q" aria-label="Words to search for in the text" style="flex:1;min-width:240px" placeholder="e.g. congé parental, breach notification, own funds">
          <input type="date" name="as_of" aria-label="Only versions in force on this date">
          <button type="submit">Search</button>
        </form></div>

        <div class="card"><h2 style="margin-top:0">What was in force on a date?</h2>
        <p class="sub">The compliance question in one call: everything that applied on a given day.</p>
        <form class="inline" action="/in-force-on" method="get">
          <input type="date" name="date" aria-label="Date to list laws in force on" value="{DateTime.UtcNow:yyyy-MM-dd}">
          <button type="submit">List it</button>
        </form></div>

        <div class="card"><h2 style="margin-top:0">What changed between two dates?</h2>
        <p class="sub">Across the whole corpus, not one law at a time — the question a compliance
        reader actually has.</p>
        <p><a href="/changed"><b>Open the change report →</b></a> ·
           <a href="/changed?from=2020-03-01&amp;to=2021-07-01&amp;order=by_churn">the pandemic, ranked by churn</a></p></div>

        <div class="card"><h2 style="margin-top:0">Browse everything</h2>
        <p class="sub">All {readers.Values.Sum(r => r.Coverage().Groups):n0} laws, by publisher and type.</p>
        <p><a href="/browse"><b>Open the catalogue →</b></a></p></div>

        <p class="sub">Not sure where to start? The <a href="/">assistant</a> takes a plain question,
        or read <a href="/stories">four laws with a story</a>.</p>
        """;
    return Results.Content(Page("Find a law", body, null, "find"), "text/html");
});

// ---- version rail: the versions of a work as marks on a time axis. Replaces a list of
// links with a shape you can read — clustering IS the amendment history.
string VersionRail(string publisher, string work, List<DocRow> versions, string? activeFrom)
{
    var vs = versions.GroupBy(v => v.ValidFrom, StringComparer.Ordinal).Select(g => g.First())
                     .OrderBy(v => v.ValidFrom, StringComparer.Ordinal).ToList();
    if (vs.Count < 2) return "";
    var ds = vs.Select(v => (v, ok: DateOnly.TryParse(v.ValidFrom, out var d), d))
               .Where(x => x.ok).Select(x => (x.v, x.d)).ToList();
    if (ds.Count < 2) return "";
    double lo = ds[0].d.DayNumber, hi = ds[^1].d.DayNumber, span = Math.Max(1, hi - lo);
    var sb = new StringBuilder("<div class=\"rail\"><div class=\"axis\"></div>");
    foreach (var (v, d) in ds)
    {
        var pct = (d.DayNumber - lo) / span * 98 + 1;
        var act = v.ValidFrom == activeFrom ? " act" : "";
        sb.Append($"<a class=\"tick{act}\" style=\"left:{pct.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}%\" "
                + $"href=\"/{H(publisher)}/{H(work)}/{H(v.ValidFrom)}\" title=\"{H(v.ValidFrom)}\"></a>");
    }
    foreach (var (v, d) in new[] { ds[0], ds[^1] })
    {
        var pct = (d.DayNumber - lo) / span * 98 + 1;
        var align = v == ds[0].v ? "left:0" : "right:0";
        sb.Append($"<span class=\"yr\" style=\"{align}\">{H(v.ValidFrom)}</span>");
    }
    sb.Append("</div>");
    sb.Append($"<p class=\"sub railcap\">{ds.Count} versions · click any mark to read the law as it stood that day"
            + (activeFrom is null ? "" : " · <span class=\"nowmark\">▌</span> the one you are reading") + "</p>");
    return sb.ToString();
}

// ---- /stories: curated point-in-time narratives. Every figure is computed from the
// mounted indexes at render time (a story that stops being true stops being shown).
app.MapGet("/stories", () =>
{
    var sb = new StringBuilder();
    sb.Append("""
        <p>Point-in-time retrieval sounds abstract until you watch a law move. These are real
        histories held by Lex — every number below is computed from the signed indexes as this
        page renders, and every link lands on the evidence.</p>
        """);

    void Story(string publisher, string work, string headline, string lede, string askQuestion)
    {
        if (!readers.TryGetValue(publisher, out var r)) return;
        // One version = one validity date. A bilingual work (DE+FR) carries two rows per
        // date; counting rows would inflate the figure a reader can check by hand.
        var vs = r.Timeline(work)
            .GroupBy(v => v.ValidFrom, StringComparer.Ordinal)
            .Select(g => g.First())
            .OrderBy(v => v.ValidFrom, StringComparer.Ordinal)
            .ToList();
        if (vs.Count == 0) return;
        var first = vs[0];
        var last = vs[^1];
        // amendment cadence: median gap between consecutive versions
        var dates = vs.Select(v => DateOnly.TryParse(v.ValidFrom, out var d) ? d : (DateOnly?)null)
                      .Where(d => d.HasValue).Select(d => d!.Value).OrderBy(d => d).ToList();
        var gaps = dates.Zip(dates.Skip(1), (a, b) => b.DayNumber - a.DayNumber).OrderBy(g => g).ToList();
        var median = gaps.Count > 0 ? gaps[gaps.Count / 2] : 0;
        var shortest = gaps.Count > 0 ? gaps[0] : 0;
        var mid = vs[vs.Count / 2];

        sb.Append($"""
            <div class="card">
              <h2 style="margin:0 0 4px">{H(headline)}</h2>
              <p class="sub" style="margin:0 0 10px">{lede}</p>
              <p><span class="badge">{vs.Count:n0} versions</span>
                 <span class="badge">{H(first.ValidFrom)} → {H(last.ValidFrom)}</span>
                 {(median > 0 ? $"<span class=\"badge\">amended every {median} days (median)</span>" : "")}
                 {(shortest > 0 ? $"<span class=\"badge\">shortest-lived version: {shortest} day{(shortest == 1 ? "" : "s")}</span>" : "")}</p>
              <p><a href="/{H(publisher)}/{H(work)}">every version</a> ·
                 <a href="/{H(publisher)}/{H(work)}/{H(first.ValidFrom)}">the first text</a> ·
                 <a href="/{H(publisher)}/{H(work)}/diff/{H(first.ValidFrom)}/{H(mid.ValidFrom)}">what changed by {H(mid.ValidFrom)}</a> ·
                 <a href="/{H(publisher)}/{H(work)}/{H(last.ValidFrom)}">the text today</a></p>
              <p class="sub">Ask the assistant: <a href="/?q={Uri.EscapeDataString(askQuestion)}">{H(askQuestion)}</a></p>
            </div>
            """);
    }

    Story("lu-legilux", "loi-2020-07-17-a624",
        "The law that could not sit still",
        "Luxembourg's Covid-19 measures act. Rules on gatherings, masks and closures were rewritten again and again — "
        + "which is exactly when \"what did the rule say <i>that week</i>?\" stops being an academic question.",
        "How did the Luxembourg Covid-19 law change between July 2020 and July 2021?");

    Story("lu-legilux", "constitution-1868-10-17-n1",
        "A constitution, revised in public",
        "The Luxembourg constitution, from the early twentieth century to the 2023 reform — the same document, "
        + "re-consolidated after every revision, each state still retrievable.",
        "What changed in the Luxembourg constitution in 2023?");

    Story("eu-eurlex", "32013r0575",
        "Banking rules in waves",
        "The Capital Requirements Regulation — the rulebook a Luxembourg bank must apply. Its own Article 92 "
        + "(the capital ratios) has more than one lifetime.",
        "How has Article 92 of the CRR changed over its life?");

    Story("lu-legilux", "loi-1879-06-18-n1",
        "The criminal code is a moving target",
        "Luxembourg's penal code has been re-consolidated repeatedly in the last decade. Point-in-time matters most "
        + "where the question is what was punishable on the day of the act.",
        "Que disait le Code pénal luxembourgeois au 1er janvier 2020 ?");

    // The same aggregate that changes_in_period exposes: the page and the API answer this
    // from one code path, so the site never knows something an MCP client cannot get.
    if (readers.TryGetValue("lu-legilux", out var luR))
    {
        var churn = luR.ChangesInPeriod("2020-03-01", "2021-07-01", null, byChurn: true, limit: 5);
        if (churn.Count > 0)
        {
            sb.Append("""
                <div class="card"><h2 style="margin:0 0 4px">Which laws moved most during the pandemic</h2>
                <p class="sub" style="margin:0 0 10px">March 2020 – July 2021, ranked by how many new versions
                each law produced. Computed live, and available to your own code as
                <span class="mono">changes_in_period(order="by_churn")</span>.</p><table>
                <tr><th>law</th><th>new versions</th><th></th></tr>
                """);
            foreach (var c in churn)
                sb.Append($"""
                    <tr><td><a href="/lu-legilux/{H(c.GroupKey)}">{H(TitleShorten(c.Title) ?? c.GroupKey)}</a></td>
                    <td class="mono">{c.VersionsInPeriod}</td>
                    <td><a href="/lu-legilux/{H(c.GroupKey)}/diff/{H(c.FirstChange)}/{H(c.LastChange)}">what changed</a></td></tr>
                    """);
            sb.Append("""
                </table><p class="sub" style="margin:8px 0 0">
                <a href="/changed?from=2020-03-01&amp;to=2021-07-01&amp;order=by_churn"><b>Explore any period →</b></a></p></div>
                """);
        }
    }

    sb.Append("""
        <div class="card"><b>The honest half.</b> A demo that only shows wins is a brochure.
          <a href="/lu-legilux/rgd-1998-08-03-n4/1900-01-01">Ask for a law in 1900</a> and Lex refuses,
          with a reason code, instead of inventing a plausible text —
          <a href="/coverage">here is exactly what it holds and what it lacks</a>.</div>
        """);
    return Results.Content(Page("Stories — watch the law move", sb.ToString(),
        "real histories from the Luxembourg and EU corpora, computed live", "find"), "text/html");
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
            $"<p>status <span class=\"mono\">no_version_for_date</span> — resolved: {da:yyyy-MM-dd}={(a is not null)}, {db2:yyyy-MM-dd}={(b is not null)}. See the <a href=\"/{H(publisher)}/{H(work)}\">timeline</a>.</p>"),
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
    return Results.Content(Page($"What changed — {H(DocTitle(b))}", sb.ToString(),
        $"{da:yyyy-MM-dd} → {db2:yyyy-MM-dd} · no interpretation, just the text delta"), "text/html");
});

app.MapGet($"/{pubRoute}/{{work}}", (string publisher, string work) =>
{
    var r = Reader(publisher);
    if (r is null) return Results.Content(Page("Unknown publisher", $"<p>No index mounted for <b>{H(publisher)}</b>. See <a href=\"/coverage\">coverage</a>.</p>"), "text/html", statusCode: 404);
    var rows = r.Timeline(work);
    if (rows.Count == 0)
        return Results.Content(Page("Unknown work", $"<p>status <span class=\"mono\">unknown_work</span> — no work <b>{H(work)}</b> in {H(publisher)}. Try <a href=\"/search\">search</a>.</p>"), "text/html", statusCode: 404);

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
            return Results.Content(Page("Unknown work", $"<p>status <span class=\"mono\">unknown_work</span> — no work <b>{H(work)}</b>. Try <a href=\"/search\">search</a>.</p>"), "text/html", statusCode: 404);
        var timeline = r.Timeline(work);
        var sb0 = new StringBuilder();
        sb0.Append($"""
            <div class="notice">status <span class="mono">no_version_for_date</span> — the work exists, but no
            version covers <b>{d:yyyy-MM-dd}</b>. The publisher's digitised history for this work covers:</div>
            """);
        sb0.Append("<ul>");
        foreach (var v in timeline.Take(30))
            sb0.Append($"<li><a href=\"/{H(publisher)}/{H(work)}/{H(v.ValidFrom)}\" class=\"mono\">{Interval(v)}</a></li>");
        sb0.Append("</ul>");
        sb0.Append(EnvelopeCard(r, IsProvisional(r, d)));
        return Results.Content(Page(H(work), sb0.ToString(), $"as of {d:yyyy-MM-dd} — honest refusal"), "text/html", statusCode: 404);
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
           <b>superseded</b> — it applied {H(Interval(doc))}. <a href="/{H(publisher)}/{H(work)}">Jump to the
           version in force today</a> or <a href="/{H(publisher)}/{H(work)}/diff/{H(doc.ValidFrom)}/{H(next.ValidFrom)}">see
           exactly what changed next</a>.</div>
           """
        : $"""
           <div class="notice" style="border-left-color:var(--ok)"><b>Point-in-time view as at {d:yyyy-MM-dd}.</b>
           This is the latest state the publisher has consolidated — valid {H(Interval(doc))}.</div>
           """);
    // Most readers arrive from a search engine straight onto this page and never see the
    // homepage. The two things they must know — what a consolidated text is, and that it
    // carries no legal force — belong here, in plain words, not only on the front door.
    sb.Append("""
        <details class="card" style="margin-top:-4px"><summary><b>New here? What am I looking at?</b></summary>
        <p>This is a <b>consolidated</b> text: the original law with every later amendment merged in,
        as the official publisher produced it for a given date. Laws are amended constantly, so
        <b>“the law” has no single text — only a text per date</b>. That date is the banner above.</p>
        <p><b>It has no legal force.</b> Only the version published in the official gazette
        (<i>Mémorial</i> / Official Journal) is authentic — the publishers say so themselves, and so do we.
        Lex reproduces their text without altering a byte, and links the source on every page.
        This is legal <i>information</i>, never legal advice: it reports what the text said,
        never what it means for your situation.</p>
        <p class="sub">“Valid from → to” = the window in which this text applied.
        “Open” = still current as far as the publisher has consolidated.
        Each article carries its own hash so you can prove it was not tampered with —
        <a href="/verify">here is how</a>.</p></details>
        """);
    sb.Append($"""
        <div class="card"><table class="kv">
        <tr><td>as of</td><td><b>{d:yyyy-MM-dd}</b> → this version applied</td></tr>
        <tr><td>valid</td><td class="mono">{Interval(doc)} <span class="badge">{H(doc.ValidTimeSource)}-asserted</span></td></tr>
        <tr><td>type</td><td><span class="badge">{H(doc.Kind)}</span> {H(doc.Title ?? "")}</td></tr>
        <tr><td>language</td><td>{H(doc.Language)}</td></tr>
        {(doc.PublicationDate is null ? "" : $"<tr><td>published</td><td class=\"mono\">{H(doc.PublicationDate)}</td></tr>")}
        <tr><td>lex_id</td><td class="mono"><a href="/provenance/{H(doc.Key)}">{H(doc.Key)}</a></td></tr>
        <tr><td>record sha256</td><td class="mono">{H(doc.RecordSha)}</td></tr>
        </table></div>
        """);
    var provisions = doc.TextPublic ? r.Provisions(LexIndexReader.RidOf(doc)) : [];
    if (provisions.Count > 0)
    {
        sb.Append($"""
            <div class="notice" style="border-left-color:var(--ok)"><b>Text included — per-article reading view.</b>
            Deterministic extraction of the verbatim retrieved document; each article carries its own hash and anchor.
            {H(r.Stamp.GetValueOrDefault("attribution"))}</div>
            <details class="card"><summary><b>Outline — {provisions.Count} provisions</b></summary><p>
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
                : string.Join(" — ", new[] { p.Num, p.Heading }.Where(s => !string.IsNullOrEmpty(s)));
            sb.Append($"""
                <div class="card" id="{H(p.Anchor)}">
                <b>{H(title)}</b>
                <a class="sub mono" href="#{H(p.Anchor)}" title="permalink to this provision">#{H(p.Anchor)}</a>
                {(p.ArticleValidFrom is not null && p.ArticleValidFrom != doc.ValidFrom ? $"<span class=\"badge\">applicable {H(p.ArticleValidFrom)}</span>" : "")}
                <pre style="white-space:pre-wrap;font:14px/1.65 Georgia,'Times New Roman',serif;margin:8px 0 0">{H(p.TextMd)}</pre>
                </div>
                """);
            shown++;
            if (shown >= 400) { sb.Append($"<p class=\"sub\">— {provisions.Count - shown:n0} further provisions omitted from this view; retrieve them via the MCP tools —</p>"); break; }
        }
    }
    else
    {
        sb.Append(TextWithheldBox(doc));
    }
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
    return Results.Content(Page(H(DocTitle(doc)), sb.ToString(), $"as it stood on {d:yyyy-MM-dd} — permalink: /{H(publisher)}/{H(work)}/{d:yyyy-MM-dd}"), "text/html");
});

app.Run();
