using System.Text;
using System.Text.Json.Nodes;
using Lex.Index;
using Lex.Mcp;
using Lex.Web;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

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
      <a href="/in-force-on">in force on…</a>&nbsp; <a href="/search">search</a>&nbsp; <a href="/ask">ask</a>&nbsp; <a href="/ai">use with your AI</a>&nbsp; <a href="/coverage">coverage</a>
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
        seven Lex tools for the law as it stood on a date, and composes its answer from returned
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

        <h2>The seven tools</h2>
        <p class="sub">as_of · timeline · in_force_on · diff · search · provenance · coverage —
        read-only, deterministic, every response carries its dates, its hash, and an honest refusal
        (<span class="mono">no_version_for_date</span>, <span class="mono">text_withheld</span>) when Lex cannot know.</p>

        <p>Want to try it without installing anything? The capped <a href="/ask">built-in playground</a>
        runs the same tools. Prefer no AI at all? Everything is also a <a href="/">permalink</a>.</p>
        """;
    return Results.Content(Page("Use Lex with your AI", body,
        "your model + our evidence — MCP endpoint, one line to connect"), "text/html");
});

// ---- /ask playground: chat over the seven tools, grounded and capped ----
app.MapGet("/ask", () =>
{
    const string body = """
        <div class="notice"><b>Not legal advice.</b> The assistant answers only from Lex's signed
        point-in-time indexes and cites the exact version (dates, hash, permalink) behind every claim.
        It reports <i>what the rule was</i>, never what it means. Consolidated texts have no legal effect.</div>

        <div id="chat"></div>
        <form id="askform" class="inline" style="margin-top:14px">
          <input id="q" style="flex:1;min-width:260px" maxlength="4000" autocomplete="off"
                 placeholder="e.g. Que disait le Code de procédure civile au 1er janvier 2020 ?">
          <button id="send" type="submit">ask</button>
        </form>
        <p class="sub" id="hints">Try:
          <a href="#" class="hint">Que disait le Code de procédure civile au 1er janvier 2020 ?</a> ·
          <a href="#" class="hint">What did the GDPR say about breach notification on 15 March 2019?</a> ·
          <a href="#" class="hint">How has DORA changed since it entered into force?</a></p>
        <p class="sub">Capped public playground (daily per-visitor and global limits). Unlimited use:
        <a href="/ai">connect your own AI</a> to the MCP endpoint.</p>

        <style>
          #chat .msg { margin:10px 0; padding:10px 14px; border-radius:10px; white-space:pre-wrap }
          #chat .u { background:var(--card); border:1px solid var(--line) }
          #chat .a { border:1px solid var(--line) }
          #chat .t { font-family:var(--mono); font-size:12.5px; color:var(--muted); margin:4px 0 4px 8px }
          #chat .err { border-left:3px solid #c0392b; padding:8px 12px; color:var(--muted) }
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
          async function ask(text) {
            msgs.push({ role: 'user', content: text });
            add('msg u', esc(text));
            const busy = add('t', 'thinking…');
            send.disabled = true;
            try {
              const r = await fetch('/api/ask', { method: 'POST', headers: { 'Content-Type': 'application/json' },
                                                 body: JSON.stringify({ messages: msgs }) });
              const j = await r.json();
              busy.remove();
              (j.trace || []).forEach(t => add('t', '→ ' + esc(t.tool) + '(' + esc(JSON.stringify(t.args)) + ')'));
              if (j.error) { add('err', esc(j.error)); msgs.pop(); }
              else { msgs.push({ role: 'assistant', content: j.reply }); add('msg a', linkify(j.reply)); }
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
        })();
        </script>
        """;
    return Results.Content(Page("Ask the AI", body,
        "a model that reads only from Lex's signed indexes — every answer cites the version it used"), "text/html");
});

app.MapPost("/api/ask", async (HttpRequest req) =>
{
    if (req.ContentLength is > 65536) return Results.Json(new { error = "Request too large." }, statusCode: 413);
    JsonNode? parsed;
    try { using var sr = new StreamReader(req.Body); parsed = JsonNode.Parse(await sr.ReadToEndAsync()); }
    catch { return Results.Json(new { error = "Bad JSON." }, statusCode: 400); }
    if (parsed?["messages"] is not JsonArray history)
        return Results.Json(new { error = "Body must be {\"messages\": [...]}." }, statusCode: 400);
    var ip = req.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim()
             ?? req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var (status, bodyJson) = await askService.AskAsync(history, ip, req.Host.Value ?? "law.soufien.lu");
    return Results.Content(bodyJson.ToJsonString(), "application/json", statusCode: status);
});

app.MapGet("/healthz", () => Results.Text("ok"));

app.MapGet("/", () =>
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
    return Results.Content(Page("Point-in-time law, honestly",
        sb.ToString(), "Luxembourg first. EU next. Every answer carries its dates, its source and its hash — never an interpretation."), "text/html");
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
        sb.Append($"""
            <div class="notice"><b>Known gaps.</b> Only the publisher's versioned (consolidated) corpus is ingested:
            {c.Groups:n0} works / {c.Rows:n0} versions. ≈24,579 never-consolidated Luxembourg acts are <b>not ingested</b>
            (their date coverage is unmeasured). Legal text bodies are not stored (metadata-only mode) — documents link
            to the official publication. History is as deep as the publisher's own digitised consolidations.</div>
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
        <form class="inline"><input name="q" value="{H(q)}" placeholder="search titles &amp; metadata" style="flex:1;min-width:240px"><button>Search</button></form>
        <p class="sub">Metadata-only mode: search covers titles and metadata, not body text. Filters run before ranking — always.</p>
        """);
    if (!string.IsNullOrWhiteSpace(q))
    {
        foreach (var r in readers.Values)
        {
            var hits = r.Search(q, new FilterSet(null, null, string.IsNullOrEmpty(kind) ? null : kind, null), 60)
                .GroupBy(h => h.Doc.GroupKey)
                .Select(g => g.First())
                .Take(15)
                .ToList();
            sb.Append($"<h2>{H(r.Stamp.GetValueOrDefault("publisher_name"))} — {hits.Count} work(s)</h2>");
            foreach (var (docRow, snippet) in hits)
                sb.Append($"""
                    <div class="card"><a href="/{H(docRow.Collection)}/{H(docRow.GroupKey)}"><b>{H(DocTitle(docRow))}</b></a>
                    <span class="badge">{H(docRow.Kind)}</span> <span class="badge mono">{Interval(docRow)}</span>
                    <div class="snippet">{snippet}</div>
                    <div class="mono sub">{H(docRow.Key)}</div></div>
                    """);
        }
    }
    return Results.Content(Page("Search", sb.ToString()), "text/html");
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
    else if (a.TextPublic && b.TextPublic && a.Body is not null && b.Body is not null)
        sb.Append(RenderDiff(a.Body, b.Body));
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
    if (doc.TextPublic && doc.Body is not null)
    {
        var display = doc.Body.Length > 300_000 ? doc.Body[..300_000] : doc.Body;
        sb.Append($"""
            <div class="notice" style="border-left-color:var(--ok)"><b>Text included — reading view.</b>
            Plain-text extraction of the verbatim retrieved document; the sha256 covers the verbatim file.
            {H(r.Stamp.GetValueOrDefault("attribution"))}</div>
            <div class="card"><pre style="white-space:pre-wrap;font:14px/1.65 Georgia,'Times New Roman',serif;margin:0">{H(display)}</pre>
            {(doc.Body.Length > 300_000 ? "<p class=\"sub\">— truncated for display; full text at the official source —</p>" : "")}</div>
            """);
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
