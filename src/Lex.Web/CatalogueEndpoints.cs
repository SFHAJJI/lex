using System.Text;
using System.Text.Json.Nodes;
using Lex.Index;
using static Lex.Web.PageShell;
using static Lex.Web.Fragments;

namespace Lex.Web;

/// <summary>
/// Finding a law: the catalogue, full-text search, what was in force on a date, what changed across a window, and the coverage this service is honest about lacking.
/// </summary>
public static class CatalogueEndpoints
{
    /// <summary>Rows per catalogue page. Enough to scan, few enough to arrive quickly.</summary>
    private const int CatalogPage = 50;

    public static IEndpointRouteBuilder MapCatalogue(this IEndpointRouteBuilder app, WebContext ctx)
    {
        // Re-declared here so every moved route body is byte-identical to what it was in
        // Program.cs. That is the property the golden snapshots check.
        string Page(string title, string body, string? subtitle = null, string nav = "",
                    string? h1 = null, string? canonicalPath = null, string? jsonLd = null,
                    string? description = null, string? lang = null)
            => PageShell.Page(ctx.PublicBase, title, body, subtitle, nav, h1, canonicalPath,
                              jsonLd, description, lang);
        var readers = ctx.Registry.All;
        var publicBase = ctx.PublicBase;
        var mcpCore = ctx.Mcp;

        app.MapGet("/browse", (HttpRequest req) =>
        {
            string? Q(string k) => req.Query[k].FirstOrDefault() is { Length: > 0 } v ? v : null;

            var pub = Q("publisher");
            var kind = Q("type");
            var text = Q("text") switch { "yes" => true, "no" => (bool?)false, _ => null };
            var order = Q("sort") switch
            {
                "versions" => CatalogueOrder.MostVersions,
                "recent" => CatalogueOrder.MostRecent,
                "oldest" => CatalogueOrder.Oldest,
                _ => CatalogueOrder.Name,
            };
            var page = int.TryParse(Q("page"), out var pg) && pg > 0 ? pg : 1;

            // One publisher's index cannot answer for another, so the catalogue is the union of the
            // mounted readers. Filtering by publisher simply narrows which ones are asked.
            var sources = readers.Values.Where(r => pub is null || r.Collection == pub).ToList();
            var filters = new FilterSet(null, null, kind, null);
            var gathered = sources
                .Select(r => r.Catalogue(filters, text, order, CatalogPage * page, 0))
                .ToList();
            var total = gathered.Sum(g => g.Total);
            // Re-sorted across publishers: each index sorted its own rows, and concatenating two sorted
            // lists does not produce a sorted list.
            IEnumerable<CatalogueRow> merged = gathered.SelectMany(g => g.Rows);
            merged = order switch
            {
                CatalogueOrder.MostVersions => merged.OrderByDescending(x => x.Versions).ThenBy(x => x.GroupKey, StringComparer.Ordinal),
                CatalogueOrder.MostRecent => merged.OrderByDescending(x => x.LastFrom, StringComparer.Ordinal).ThenBy(x => x.GroupKey, StringComparer.Ordinal),
                CatalogueOrder.Oldest => merged.OrderBy(x => x.FirstFrom, StringComparer.Ordinal).ThenBy(x => x.GroupKey, StringComparer.Ordinal),
                _ => merged.OrderBy(x => x.GroupKey, StringComparer.Ordinal),
            };
            var rows = merged.Skip((page - 1) * CatalogPage).Take(CatalogPage).ToList();
            var pages = Math.Max(1, (total + CatalogPage - 1) / CatalogPage);

            string Link(string k, string? v)
            {
                var q = new List<string>();
                void Keep(string name, string? cur) { if (cur is not null) q.Add($"{name}={Uri.EscapeDataString(cur)}"); }
                Keep("publisher", k == "publisher" ? v : pub);
                Keep("type", k == "type" ? v : kind);
                Keep("text", k == "text" ? v : text switch { true => "yes", false => "no", _ => null });
                Keep("sort", k == "sort" ? v : Q("sort"));
                Keep("page", k == "page" ? v : null);
                return "/browse" + (q.Count > 0 ? "?" + string.Join("&amp;", q) : "");
            }

            var sb = new StringBuilder();
            sb.Append($"""
                <p class="sub" style="margin-top:0">Every work Lex holds, with the number of dated versions
                behind it. A work is a law; a version is what that law said between two dates.
                <b>{total:n0}</b> works match.</p>
                """);

            // Filters are links, not a form: no JavaScript, and every state of this page is a URL that a
            // reader can bookmark, share, or hand to a crawler.
            sb.Append("""<nav class="filters" aria-label="Narrow the catalogue">""");
            sb.Append($"""<div><i>publisher</i><a class="f{(pub is null ? " on" : "")}" href="{Link("publisher", null)}">all</a>""");
            foreach (var r in readers.Values)
                sb.Append($"""<a class="f{(pub == r.Collection ? " on" : "")}" href="{Link("publisher", r.Collection)}">{H(r.Stamp.GetValueOrDefault("publisher_name", r.Collection))}</a>""");
            sb.Append("</div>");

            var kinds = sources.SelectMany(r => r.CatalogueKinds(null))
                               .GroupBy(x => x.Kind)
                               .Select(g => (Kind: g.Key, Works: g.Sum(x => x.Works)))
                               .OrderByDescending(x => x.Works).Take(14).ToList();
            sb.Append($"""<div><i>type</i><a class="f{(kind is null ? " on" : "")}" href="{Link("type", null)}">all</a>""");
            foreach (var (k, n) in kinds)
                sb.Append($"""<a class="f{(kind == k ? " on" : "")}" href="{Link("type", k)}">{H(k)} <span class="n">{n:n0}</span></a>""");
            sb.Append("</div>");

            sb.Append($"""
                <div><i>text</i>
                <a class="f{(text is null ? " on" : "")}" href="{Link("text", null)}">any</a>
                <a class="f{(text == true ? " on" : "")}" href="{Link("text", "yes")}">full text held</a>
                <a class="f{(text == false ? " on" : "")}" href="{Link("text", "no")}">record only</a></div>
                <div><i>sort</i>
                <a class="f{(order == CatalogueOrder.Name ? " on" : "")}" href="{Link("sort", null)}">name</a>
                <a class="f{(order == CatalogueOrder.MostVersions ? " on" : "")}" href="{Link("sort", "versions")}">most versions</a>
                <a class="f{(order == CatalogueOrder.MostRecent ? " on" : "")}" href="{Link("sort", "recent")}">most recent</a>
                <a class="f{(order == CatalogueOrder.Oldest ? " on" : "")}" href="{Link("sort", "oldest")}">oldest first</a></div>
                </nav>
                """);

            if (rows.Count == 0)
            {
                sb.Append("""<div class="card"><p>No work matches those filters. <a href="/browse">Clear them</a>.</p></div>""");
            }
            else
            {
                sb.Append("""
                    <div class="card" style="overflow-x:auto"><table class="cat">
                    <tr><th>work</th><th>type</th><th class="r">versions</th><th>first</th><th>last</th><th>text</th></tr>
                    """);
                foreach (var w in rows)
                {
                    var mark = w.HasText
                        ? """<span class="badge ok">full text</span>"""
                        : """<span class="badge">record only</span>""";
                    sb.Append($"""
                        <tr>
                          <td><a href="/{H(w.Collection)}/{H(w.GroupKey)}">{H(w.TitleShort ?? w.Title ?? w.GroupKey)}</a>
                              <div class="sub mono">{H(w.GroupKey)}</div></td>
                          <td class="mono">{H(w.Kind ?? "")}</td>
                          <td class="r">{w.Versions:n0}</td>
                          <td class="mono">{H(w.FirstFrom)}</td>
                          <td class="mono">{H(w.LastFrom)}</td>
                          <td>{mark}</td>
                        </tr>
                        """);
                }
                sb.Append("</table></div>");

                if (pages > 1)
                {
                    sb.Append("""<nav class="pager" aria-label="Pages">""");
                    if (page > 1) sb.Append($"""<a href="{Link("page", (page - 1).ToString())}">&larr; previous</a>""");
                    sb.Append($"""<span>page {page:n0} of {pages:n0}</span>""");
                    if (page < pages) sb.Append($"""<a href="{Link("page", (page + 1).ToString())}">next &rarr;</a>""");
                    sb.Append("</nav>");
                }
            }

            // The curated links keep their home, below the thing they used to stand in for.
            sb.Append("""
                <h2>Start here</h2>
                <ul>
                  <li><a href="/lu-legilux/rgd-1998-08-03-n4/2018-01-01">Nouveau Code de proc&#233;dure civile, as it stood on 1 Jan 2018</a></li>
                  <li><a href="/lu-legilux/code-environnement">Code de l'environnement, full timeline</a></li>
                  <li><a href="/lu-legilux/recueil-protection_donnees">Recueil protection des donn&#233;es, timeline</a></li>
                  <li><a href="/in-force-on?date=2022-03-15&amp;kind=CODE">Which codes were in force on 15 Mar 2022?</a></li>
                  <li><a href="/eu-eurlex/32013r0575">CRR (EU) 575/2013-22 consolidated versions, incl. future-dated</a></li>
                  <li><a href="/eu-eurlex/32016r0679/2019-01-01">GDPR as it stood on 1 Jan 2019, with full text</a></li>
                  <li><a href="/eu-eurlex/32013r0575/diff/2020-01-01/2024-01-01">CRR: what changed between 2020 and 2024?</a></li>
                </ul>
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
                    <span class="badge">{H(c.EarliestValidFrom)} &rarr; {H(c.LatestValidFrom)}</span>
                    <span class="badge {(r.SignatureValid ? "ok" : "warn")}">{(r.SignatureValid ? "signed index" : "unsigned")}</span>
                    <div class="sub" style="margin-top:6px">Dense and reliable from 2017 onward; real but sparse before; isolated snapshots back to 1849; forward to 2030.</div>
                    </div>
                    """);
            }
            sb.Append("""
                <h2>Look something up</h2>
                <form class="inline" action="/search"><input name="q" placeholder="words in the text, e.g. protection des donn&#233;es" style="flex:1;min-width:240px"><button>Search</button></form>
                <form class="inline" action="/go-asof">
                  <input name="work" placeholder="work slug, e.g. code-travail" style="flex:1;min-width:200px">
                  <input name="date" type="date" value="2022-03-15">
                  <button>As of date</button>
                </form>
                """);
            // Dataset, because this is what Google Dataset Search indexes, and a CC-BY corpus of
            // consolidated national law with a stated temporal range is exactly what that index
            // is for. Built as a JsonObject: JSON is mostly quotes and braces, which is what a
            // C# raw literal reserves, and hand-quoting it ships malformed markup silently.
            var span = ctx.Registry.Values.Select(r => r.Coverage()).ToList();
            var datasetLd = new JsonObject
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "Dataset",
                ["name"] = "Lex: point-in-time Luxembourg law and ten EU acts",
                ["description"] = "Every consolidated version of Luxembourg law that Legilux "
                    + "publishes, plus ten EU acts, as dated records carrying validity intervals, "
                    + "per-article history, and a SHA-256 chain to the publisher's own bytes.",
                ["url"] = $"{ctx.PublicBase}/browse",
                ["license"] = "https://creativecommons.org/licenses/by/4.0/",
                ["isAccessibleForFree"] = true,
                ["creator"] = new JsonObject
                {
                    ["@type"] = "Person", ["name"] = "Soufien Hajji", ["url"] = "https://soufien.lu",
                },
                ["keywords"] = new JsonArray("legislation", "Luxembourg", "European Union",
                    "consolidated law", "point-in-time", "legal data", "open data"),
                ["temporalCoverage"] =
                    $"{span.Select(c => c.EarliestValidFrom).Min()}/{span.Select(c => c.LatestValidFrom).Max()}",
                ["spatialCoverage"] = new JsonArray(
                    new JsonObject { ["@type"] = "Country", ["name"] = "Luxembourg" },
                    new JsonObject { ["@type"] = "Place", ["name"] = "European Union" }),
                ["distribution"] = new JsonArray(
                    new JsonObject
                    {
                        ["@type"] = "DataDownload", ["encodingFormat"] = "application/json",
                        ["contentUrl"] = "https://github.com/SFHAJJI/lex-articles",
                        ["description"] = "Per-article JSON, JSONL and parquet, CC-BY.",
                    },
                    new JsonObject
                    {
                        ["@type"] = "DataDownload", ["encodingFormat"] = "application/json",
                        ["contentUrl"] = $"{ctx.PublicBase}/mcp",
                        ["description"] = "Public MCP endpoint, ten read-only tools, no key.",
                    }),
            }.ToJsonString();

            return Results.Content(Page("The catalogue",
                sb.ToString(), "Every work Lex holds, filterable by publisher, type and whether the text itself is held.",
                "browse", canonicalPath: "/browse", jsonLd: datasetLd), "text/html");
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
                // Per type, held versus readable. The second number is the one that matters, and it is
                // computed from the index rather than written down, so it cannot drift from the corpus.
                sb.Append("<div class=\"card\"><table><tr><th>document type</th><th>versions</th>"
                        + "<th>with text</th><th></th></tr>");
                foreach (var k in c.Kinds)
                {
                    var pct = k.Versions > 0 ? 100.0 * k.WithText / k.Versions : 0;
                    var folder = k.Kind is "RECUEIL" or "CODE_RECUEIL";
                    sb.Append($"""
                        <tr><td>{H(k.Kind ?? "(untyped)")}</td><td>{k.Versions:n0}</td>
                        <td>{k.WithText:n0} <span class="mono" style="opacity:.6">{pct:0}%</span></td>
                        <td>{(folder ? "<span class=\"badge\">thematic folder, not an instrument</span>" : "")}</td></tr>
                        """);
                }
                // The confidence mix, as a count rather than a claim. Text from publisher markup and text
                // read out of a PDF are different evidence, and the page that reports coverage is the page
                // that should say how much of it is which.
                if (c.Profiles.Count > 0)
                {
                    sb.Append("<div class=\"card\"><table><tr><th>how the text was obtained</th><th>versions</th></tr>");
                    foreach (var pr in c.Profiles)
                    {
                        var what = pr.Profile switch
                        {
                            "akn-lu/1" => "publisher XML (Akoma Ntoso), article boundaries from the publisher",
                            "fmx4-eu/1" => "publisher XML (Formex 4), article boundaries from the publisher",
                            "xhtml-eu/1" => "publisher XHTML, article boundaries from the publisher",
                            "pdf-lu/1" => "read from the publisher's PDF, article boundaries inferred from layout",
                            "pdf-memorial-lu/1" => "cut out of an official gazette issue, both the act's boundaries and its articles inferred",
                            _ => "",
                        };
                        sb.Append($"<tr><td class=\"mono\">{H(pr.Profile)}</td><td>{pr.Versions:n0}</td></tr>"
                                + (what.Length > 0 ? $"<tr><td colspan=\"2\" class=\"sub\">{what}</td></tr>" : ""));
                    }
                    sb.Append("</table></div>");
                }
                sb.Append($"</table></div>{EnvelopeCard(r, false)}");
                var luGap = c.Collection == "lu-legilux"
                    ? """
                      The publisher only maintains consolidated (amendments-merged) editions for some laws , 
                      the codes and frequently amended acts. Lex holds <b>all of those</b>. The other
                      ≈24,579 Luxembourg acts never get a consolidated edition; they are <b>not here yet</b>
                      (and we won't guess dates for texts we haven't seen).
                      """
                    : " Only flagship acts are ingested so far; the wider consolidated acquis is scheduled.";
                // Measured against the publisher's own catalogue, 2026-08-04. The cause is the file format
                // offered per version, not our pipeline and not the age of the act, and it lands mostly on
                // documents that are not instruments at all.
                var gapWhy = c.Collection == "lu-legilux"
                    ? """
                      <b>Why:</b> Lex ingests the publisher's XML, because XML is the only format that marks
                      where each article begins and ends, which is what makes an article citable, hashable and
                      comparable across dates. Legilux offers XML for 2,892 of its consolidations, PDF only for
                      1,611, and no file at all for 130. Roughly 1,371 of those PDF-only versions are the
                      thematic folders marked above, which nobody voted and which carry no rule of their own.
                      Across every genuine instrument, from the Constitution down to ministerial regulations,
                      about 240 versions lack text. The wording still exists at the publisher, and each of these
                      entries links to it.
                      """
                    : "";
                sb.Append($"""
                    <div class="notice"><b>What we hold, and what we honestly don't.</b>
                    {c.Groups:n0} laws in {c.Rows:n0} dated snapshots.{luGap}
                    Of those snapshots, <b>{c.TextServed:n0}</b> carry the full official text and
                    <b>{c.Rows - c.TextServed:n0}</b> are a dated entry with its source and hash but no wording.
                    {gapWhy}
                    Those answer with <span class="mono">text_withheld</span> rather than pretending.
                    History can never go deeper than what the publisher itself digitised.</div>
                    """);
            }
            return Results.Content(Page("Coverage, what we hold, and what we lack", sb.ToString()), "text/html");
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
                    sb.Append($"<h2>{H(r.Stamp.GetValueOrDefault("publisher_name"))}, {total:n0} works in force on {d:yyyy-MM-dd}</h2>");
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
                        ≈24,579 never-consolidated LU acts are not ingested (date coverage unmeasured), see <a href="/coverage">coverage</a>.</div>
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
                <p class="sub">Article-level full-text search over every held provision. Filters run before ranking, always.</p>
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
                    sb.Append($"<h2>{H(r.Stamp.GetValueOrDefault("publisher_name"))}, {hits.Count} hit(s)</h2>");
                    foreach (var (docRow, prov, snippet) in hits)
                        sb.Append($"""
                            <div class="card"><a href="/{H(docRow.Collection)}/{H(docRow.GroupKey)}/{H(docRow.ValidFrom)}#{H(prov.Anchor)}"><b>{H(DocTitle(docRow))}</b>
                            ,  {H(prov.Num ?? prov.Heading ?? prov.Anchor)}</a>
                            <span class="badge">{H(docRow.Kind)}</span> <span class="badge mono">{Interval(docRow)}</span>
                            <div class="snippet">{H(snippet)}</div>
                            <div class="mono sub">{H(prov.ProvisionId)}</div></div>
                            """);
                }
            }
            return Results.Content(Page("Search", sb.ToString()), "text/html");
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
                <p class="lede">Every law in Lex that gained a new version between two
                dates, the corpus-wide view that a single law's timeline cannot give you.</p>
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
                blocks.Append($"<h2>{H(r.Collection)}, {works:n0} law(s) moved, {versions:n0} new version(s)</h2>");
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
                  {(totalWorks == 0 ? "Nothing moved in this window, which is itself an answer." : "")}
                </div>
                """);
            sb.Append(blocks);
            sb.Append($"""
                <p class="sub">Same data, from your own code:
                <span class="mono">changes_in_period(from_date="{H(f)}", to_date="{H(t)}"{(byChurn ? ", order=\"by_churn\"" : "")})</span>
                ,  <a href="/developers">try it in the browser</a>.</p>
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
                <p class="sub">Finds the individual article, not just the law, search runs over every provision.</p>
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
                <p class="sub">Across the whole corpus, not one law at a time, the question a compliance
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

        return app;
    }
}
