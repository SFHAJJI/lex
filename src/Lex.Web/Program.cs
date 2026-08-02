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

string Page(string title, string body, string? subtitle = null) => $$"""
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
      :root { --bg:#ffffff; --fg:#16181d; --muted:#5c6470; --line:#e3e6ea; --accent:#0b57d0;
              --card:#f6f7f9; --ok:#0a7a3d; --warn:#a15c00; --mono:ui-monospace,'Cascadia Code',Consolas,monospace; }
      @media (prefers-color-scheme: dark) {
        :root { --bg:#101317; --fg:#e8eaee; --muted:#9aa3af; --line:#2a2f36; --accent:#8ab4f8;
                --card:#1a1e24; --ok:#5bd18f; --warn:#f0b35c; } }
      * { box-sizing:border-box }
      body { margin:0; font:16px/1.55 system-ui,-apple-system,'Segoe UI',Roboto,sans-serif; background:var(--bg); color:var(--fg); }
      a { color:var(--accent); text-decoration:none } a:hover { text-decoration:underline }
      header { border-bottom:1px solid var(--line); padding:14px 20px; display:flex; gap:14px; align-items:baseline; flex-wrap:wrap }
      header .brand { font-weight:700; font-size:19px; color:var(--fg) }
      header .tag { color:var(--muted); font-size:13.5px }
      main { max-width:960px; margin:0 auto; padding:26px 20px 60px }
      h1 { font-size:24px; margin:0 0 4px } h2 { font-size:17px; margin:26px 0 8px }
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
      button { background:var(--accent); color:#fff; border-color:var(--accent); cursor:pointer }
      footer { border-top:1px solid var(--line); margin-top:40px; padding:16px 20px; color:var(--muted); font-size:13px }
      .notice { border-left:3px solid var(--warn); padding:10px 14px; background:var(--card); border-radius:0 8px 8px 0; margin:12px 0; font-size:14.5px }
      .snippet { color:var(--muted); font-size:13.5px }
    </style></head>
    <body>
    <header>
      <a class="brand" href="/">Lex</a>
      <span class="tag">point-in-time regulatory text — what did the rule say on a given date?</span>
      <span style="flex:1"></span>
      <a href="/stories">stories</a>&nbsp; <a href="/browse">browse</a>&nbsp; <a href="/search">search</a>&nbsp; <a href="/in-force-on">in force on…</a>&nbsp; <a href="/architecture">architecture</a>&nbsp; <a href="/ai">use with your AI</a>&nbsp; <a href="/verify">verify</a>&nbsp; <a href="/coverage">coverage</a>&nbsp; <a href="https://github.com/SFHAJJI/lex" rel="noopener">github</a>
    </header>
    <main>
    <h1>{{title}}</h1>
    {{(subtitle is null ? "" : $"<p class=\"sub\">{subtitle}</p>")}}
    {{body}}
    </main>
    <footer>
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
        foreach (var l in removed) sb.Append($"<span style=\"color:#c0392b\">− {H(Trunc(l))}</span>\n");
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
        { sb.Append($"<span style=\"color:#c0392b\">− {H(Trunc(o[x]))}</span>\n"); x++; emitted++; }
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
        eight Lex tools for the law as it stood on a date, and composes its answer from returned
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

        <h2>The eight tools</h2>
        <p class="sub">as_of (full/outline/select) · timeline · in_force_on · diff · search ·
        provenance · article_history · coverage —
        read-only, deterministic, every response carries its dates, its hash, and an honest refusal
        (<span class="mono">no_version_for_date</span>, <span class="mono">text_withheld</span>) when Lex cannot know.</p>

        <h2>Azure AI Foundry agents</h2>
        <div class="card">Foundry's Agent Service speaks remote MCP natively — point an agent at this
        endpoint and it gets all eight tools (no key needed; leave approvals on for writes-free comfort):
        <pre class="mono" style="white-space:pre-wrap">{ "type": "mcp", "server_label": "lex", "server_url": "{{baseUrl}}/mcp", "require_approval": "never" }</pre></div>

        <p>Want to try it without installing anything? The capped <a href="/ask">built-in playground</a>
        runs the same tools. Prefer no AI at all? Everything is also a <a href="/">permalink</a>.</p>
        """;
    return Results.Content(Page("Use Lex with your AI", body,
        "your model + our evidence — MCP endpoint, one line to connect"), "text/html");
});

// ---- /ask playground: chat over the seven tools, grounded and capped ----
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
        <span class="badge">8 MCP tools</span>
        <span class="badge">Luxembourg + EU</span></p>
        """;
    var body = strip + """
        <div class="notice"><b>AI answers, deterministic evidence — not legal advice.</b> The assistant reads only
        Lex's signed point-in-time indexes; under every answer, the evidence cards show what the tools actually
        returned (exact version, dates, hash, permalink). It reports <i>what the rule was</i>, never what it means.
        Consolidated texts have no legal effect.</div>

        <div id="chat"></div>
        <form id="askform" class="inline" style="margin-top:14px">
          <input id="q" style="flex:1;min-width:260px" maxlength="4000" autocomplete="off"
                 placeholder="e.g. Que disait le Code de procédure civile au 1er janvier 2020 ?">
          <button id="send" type="submit">ask</button>
        </form>
        <p class="sub" id="hints">Try:
          <a href="#" class="hint">Que disait le Code de procédure civile au 1er janvier 2020 ?</a> ·
          <a href="#" class="hint">What did the GDPR say about breach notification on 15 March 2019?</a> ·
          <a href="#" class="hint">How has Article 92 of the CRR changed over its life?</a> ·
          <a href="#" class="hint">Which Luxembourg codes were in force on 15 March 2022?</a></p>

        <h2>Or skip the AI entirely</h2>
        <div class="card"><b>Time-travel by hand.</b> Every version is a permalink; every claim is a click.
          <a href="/eu-eurlex/32016r0679/2019-01-01">GDPR as it stood on 1 Jan 2019</a> ·
          <a href="/eu-eurlex/32013r0575/diff/2020-01-01/2024-01-01">CRR: what changed 2020→2024</a> ·
          <a href="/lu-legilux/code-environnement">a code's 195 versions</a> ·
          <a href="/lu-legilux/rgd-1998-08-03-n4/1900-01-01">ask for 1900 and watch it refuse honestly</a> ·
          <a href="/browse">browse everything</a></div>
        <div class="card"><b>Bring your own AI.</b> The same eight tools, hosted, no key:
          <pre class="mono" style="white-space:pre-wrap;margin:6px 0 0">claude mcp add --transport http lex https://law.soufien.lu/mcp</pre>
          <a href="/ai">all MCP clients →</a></div>
        <div class="card"><b>For developers.</b> <a href="/architecture">How it works</a> — the evidence layer,
          the derived per-article layer, the signed indexes, the honest refusals — and
          <a href="/verify">how to verify every byte without trusting us</a>.
          Data: <a href="https://github.com/SFHAJJI/lex-articles">lex-articles</a> (machine-readable, CC-BY).</div>
        <p class="sub">Capped public playground (daily per-visitor and global limits). Unlimited use:
        <a href="/ai">connect your own AI</a> to the MCP endpoint.</p>

        <style>
          #chat .msg { margin:10px 0; padding:10px 14px; border-radius:10px; white-space:pre-wrap }
          #chat .u { background:var(--card); border:1px solid var(--line) }
          #chat .a { border:1px solid var(--line) }
          #chat .t { font-family:var(--mono); font-size:12.5px; color:var(--muted); margin:4px 0 4px 8px }
          #chat .err { border-left:3px solid #c0392b; padding:8px 12px; color:var(--muted) }
          #chat .ev { border:1px solid var(--line); border-radius:10px; margin:6px 0 14px; padding:8px 12px; font-size:13.5px }
          #chat .ev h4 { margin:0 0 6px; font-size:13px; color:var(--muted) }
          #chat .ev .evcard { border-top:1px solid var(--line); padding:6px 0 }
          #chat .ev .stat { border:1px solid var(--line); border-radius:99px; padding:0 8px; font-size:11.5px; color:var(--muted) }
          #chat .ev .stat.warn { color:var(--warn); border-color:var(--warn) }
          #chat .ev .evver { margin:4px 0 4px 10px }
          #chat .ev .pin { margin:4px 0 4px 14px; border-left:2px solid var(--line); padding-left:8px }
          #chat .ev .pin .q { color:var(--muted); font-style:italic }
          #chat .share { margin:-6px 0 14px 2px; font-size:13.5px }
        </style>
        <script>
        (function () {
          const chat = document.getElementById('chat'), q = document.getElementById('q'),
                form = document.getElementById('askform'), send = document.getElementById('send');
          const msgs = [];
          function esc(s) { const d = document.createElement('div'); d.textContent = s; return d.innerHTML; }
          function linkify(s) {
            return esc(s).replace(/(https?:\/\/[^\s)"'<>]+)/g, '<a href="$1" rel="noopener">$1</a>');
          }
          function add(cls, html) { const d = document.createElement('div'); d.className = cls; d.innerHTML = html; chat.appendChild(d); d.scrollIntoView({block:'end'}); return d; }
          // Every question is a URL: ?q=... re-runs it against the same signed indexes,
          // so an answer can be pasted into a chat, a mail, a paper.
          function shareLink(text) { return location.origin + '/?q=' + encodeURIComponent(text); }
          async function ask(text) {
            msgs.push({ role: 'user', content: text });
            add('msg u', esc(text));
            if (msgs.length === 1) history.replaceState(null, '', '/?q=' + encodeURIComponent(text));
            const busy = add('t', 'thinking…');
            send.disabled = true;
            try {
              const r = await fetch('/api/ask', { method: 'POST', headers: { 'Content-Type': 'application/json' },
                                                 body: JSON.stringify({ messages: msgs }) });
              const j = await r.json();
              busy.remove();
              (j.trace || []).forEach(t => add('t', '→ ' + esc(t.tool) + '(' + esc(JSON.stringify(t.args)) + ')'));
              if (j.error) { add('err', esc(j.error)); msgs.pop(); }
              else {
                msgs.push({ role: 'assistant', content: j.reply });
                add('msg a', linkify(j.reply));
                const calls = (j.trace || []).filter(t => (t.docs || []).length || t.status);
                if (calls.length) {
                  // Evidence tree: work -> point-in-time version -> pinpointed articles with
                  // the exact text the tools returned. A reader can compare each claim above
                  // against source text without leaving the page (misgrounding is the failure
                  // mode legal AI is known for — a real citation that doesn't support the claim).
                  let html = '<h4>Evidence — what the tools returned (deterministic; the AI text above is generated)</h4>';
                  const works = new Map();
                  calls.forEach(t => {
                    const warn = t.status && t.status !== 'ok' ? ' warn' : '';
                    html += '<span class="stat' + warn + '">' + esc(t.tool)
                          + (t.status ? ' · ' + esc(t.status) : '') + '</span> ';
                    (t.docs || []).forEach(d => {
                      const wk = (d.lex_id || '').split(':').slice(0, 2).join(':') || d.title || '?';
                      if (!works.has(wk)) works.set(wk, { title: d.title, versions: new Map() });
                      const w = works.get(wk); if (!w.title && d.title) w.title = d.title;
                      const vk = d.valid_from || '?';
                      if (!w.versions.has(vk)) w.versions.set(vk, { to: d.valid_to, link: d.permalink, pins: [] });
                      const v = w.versions.get(vk);
                      (d.pinpoints || []).forEach(p => { if (!v.pins.some(x => x.anchor === p.anchor)) v.pins.push(p); });
                      if (d.snippet) v.pins.push({ anchor: d.anchor, quote: d.snippet, permalink: d.permalink });
                    });
                  });
                  works.forEach((w, wk) => {
                    html += '<div class="evcard"><b>' + esc(w.title || wk) + '</b>';
                    w.versions.forEach((v, from) => {
                      const stamp = 'point-in-time: ' + esc(from) + ' → ' + esc(v.to || 'open');
                      html += '<div class="evver"><span class="stat">' + stamp + '</span>'
                            + (v.link ? ' <a href="' + esc(v.link) + '" rel="noopener">open this version</a>' : '') ;
                      v.pins.slice(0, 4).forEach(p => {
                        html += '<div class="pin">'
                              + (p.anchor ? (p.permalink ? '<a href="' + esc(p.permalink) + '" rel="noopener">#' + esc(p.anchor) + '</a> ' : '<b>#' + esc(p.anchor) + '</b> ') : '')
                              + (p.quote ? '<span class="q">' + esc(p.quote) + '</span>' : '') + '</div>';
                      });
                      html += '</div>';
                    });
                    html += '</div>';
                  });
                  add('ev', html);
                }
                const first = msgs.find(m => m.role === 'user');
                if (first) add('share', '<a href="' + esc(shareLink(first.content)) + '">🔗 share this answer</a> '
                  + '<span class="sub">— the link re-runs the question against the same signed indexes</span>');
              }
            } catch (e) { busy.remove(); add('err', 'network error — try again'); msgs.pop(); }
            send.disabled = false; q.focus();
          }
          form.addEventListener('submit', function (e) {
            e.preventDefault();
            const text = q.value.trim(); if (!text || send.disabled) return;
            q.value = ''; ask(text);
          });
          document.querySelectorAll('.hint').forEach(a => a.addEventListener('click', function (e) {
            e.preventDefault(); if (!send.disabled) ask(this.textContent);
          }));
          // Shared link (?q=) or a story link: run it immediately on arrival.
          const preset = new URLSearchParams(location.search).get('q');
          if (preset && preset.trim()) ask(preset.trim().slice(0, 4000));
        })();
        </script>
        """;
    return Results.Content(Page("Ask Luxembourg + EU law, point-in-time", body,
        "what did the rule say on that date? — AI answers grounded in signed, verifiable, per-article indexes"), "text/html");
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
                            this site · /mcp (8 tools, any MCP client) · datasets</pre></div>

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
                    <div class="snippet">{snippet}</div>
                    <div class="mono sub">{H(prov.ProvisionId)}</div></div>
                    """);
        }
    }
    return Results.Content(Page("Search", sb.ToString()), "text/html");
});

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

    sb.Append("""
        <div class="card"><b>The honest half.</b> A demo that only shows wins is a brochure.
          <a href="/lu-legilux/rgd-1998-08-03-n4/1900-01-01">Ask for a law in 1900</a> and Lex refuses,
          with a reason code, instead of inventing a plausible text —
          <a href="/coverage">here is exactly what it holds and what it lacks</a>.</div>
        """);
    return Results.Content(Page("Stories — watch the law move", sb.ToString(),
        "four real histories from the Luxembourg and EU corpora, computed live"), "text/html");
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

app.MapGet("/{publisher}/{work}/diff/{dateA}/{dateB}", (string publisher, string work, string dateA, string dateB) =>
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

app.MapGet("/{publisher}/{work}", (string publisher, string work) =>
{
    var r = Reader(publisher);
    if (r is null) return Results.Content(Page("Unknown publisher", $"<p>No index mounted for <b>{H(publisher)}</b>. See <a href=\"/coverage\">coverage</a>.</p>"), "text/html", statusCode: 404);
    var rows = r.Timeline(work);
    if (rows.Count == 0)
        return Results.Content(Page("Unknown work", $"<p>status <span class=\"mono\">unknown_work</span> — no work <b>{H(work)}</b> in {H(publisher)}. Try <a href=\"/search\">search</a>.</p>"), "text/html", statusCode: 404);

    var t = DocTitle(rows[^1]);
    var sb = new StringBuilder();
    sb.Append($"<p><span class=\"badge\">{H(rows[^1].Kind)}</span> <span class=\"badge\">{rows.Count} version(s)</span> <a class=\"badge\" href=\"{H(rows[^1].SourceUri)}\">official text ↗</a></p>");
    sb.Append("<div class=\"card\"><table><tr><th>valid</th><th>as-of view</th><th>status</th><th>provenance</th></tr>");
    foreach (var v in rows)
        sb.Append($"""
            <tr><td class="mono">{Interval(v)}</td>
            <td><a href="/{H(publisher)}/{H(work)}/{H(v.ValidFrom)}">as of {H(v.ValidFrom)}</a></td>
            <td>{(v.ValidTo is null ? "<span class=\"badge ok\">open</span>" : "<span class=\"badge\">superseded</span>")}</td>
            <td><a class="mono" href="/provenance/{H(v.Key)}">{H(v.Key.Split(':')[^1])}</a></td></tr>
            """);
    sb.Append("</table></div>");
    sb.Append("<p class=\"sub\">Every state this document has been in, as asserted by the publisher. The corpus repo's <span class=\"mono\">git log</span> for this work shows the same history.</p>");
    sb.Append(EnvelopeCard(r, false));
    return Results.Content(Page(t, sb.ToString(), $"timeline — {H(work)}"), "text/html");
});

app.MapGet("/{publisher}/{work}/{date}", (string publisher, string work, string date) =>
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
    return Results.Content(Page(DocTitle(doc), sb.ToString(), $"as it stood on {d:yyyy-MM-dd} — permalink: /{H(publisher)}/{H(work)}/{d:yyyy-MM-dd}"), "text/html");
});

app.Run();
