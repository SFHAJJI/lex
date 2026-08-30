using System.Diagnostics.CodeAnalysis;
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
                    string? description = null, string? lang = null, bool assistant = true,
                    string? extraHead = null)
            => PageShell.Page(ctx.PublicBase, title, body, subtitle, nav, h1, canonicalPath,
                              jsonLd, description, lang, ctx.Options.CodeCommit, assistant, extraHead);
        const string NoIndexFollow = "<meta name=\"robots\" content=\"noindex,follow\">";
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
                _ => merged.OrderBy(x => x.TitleShort ?? x.Title ?? x.GroupKey, StringComparer.OrdinalIgnoreCase)
                           .ThenBy(x => x.GroupKey, StringComparer.Ordinal),
            };
            var rows = merged.Skip((page - 1) * CatalogPage).Take(CatalogPage).ToList();
            var pages = Math.Max(1, (total + CatalogPage - 1) / CatalogPage);

            string Link(string k, string? v)
            {
                var q = new List<string>();
                void Keep(string name, string? cur) { if (cur is not null) q.Add($"{name}={Uri.EscapeDataString(cur)}"); }
                Keep("publisher", k == "publisher" ? v : pub);
                // Source classes belong to a publisher vocabulary. Carrying a Luxembourg code
                // such as CODE_RECUEIL into EUR-Lex creates an empty catalogue that looks like a
                // data outage, so changing jurisdiction clears that dependent facet.
                Keep("type", k == "publisher" ? null : k == "type" ? v : kind);
                Keep("text", k == "text" ? v : text switch { true => "yes", false => "no", _ => null });
                Keep("sort", k == "sort" ? v : Q("sort"));
                Keep("page", k == "page" ? v : null);
                return "/browse" + (q.Count > 0 ? "?" + string.Join("&amp;", q) : "");
            }

            string KindLink(string collection, string sourceClass)
            {
                var q = new List<string>
                {
                    $"publisher={Uri.EscapeDataString(collection)}",
                    $"type={Uri.EscapeDataString(sourceClass)}",
                };
                if (text is not null) q.Add($"text={(text == true ? "yes" : "no")}");
                if (Q("sort") is { } sort) q.Add($"sort={Uri.EscapeDataString(sort)}");
                return "/browse?" + string.Join("&amp;", q);
            }

            var sb = new StringBuilder();
            sb.Append($"""
                <p class="sub" style="margin:0 0 2px"><b>{total:n0}</b> works match.
                <a href="/coverage">What counts as a work and a dated version?</a><br>
                <b>Record only:</b> identity and timeline held; no searchable provision text.</p>
                """);

            // Filters are links, not a form: no JavaScript, and every state of this page is a URL that a
            // reader can bookmark, share, or hand to a crawler.
            sb.Append("""<nav class="filters" aria-label="Narrow the catalogue">""");
            sb.Append($"""<div><i>publisher</i><a class="f{(pub is null ? " on" : "")}" href="{Link("publisher", null)}">all</a>""");
            foreach (var r in readers.Values)
                sb.Append($"""<a class="f{(pub == r.Collection ? " on" : "")}" href="{Link("publisher", r.Collection)}">{H(r.Stamp.GetValueOrDefault("publisher_name", r.Collection))}</a>""");
            sb.Append("</div>");

            sb.Append($"""<div class="type-filter"><i>source class</i><a class="f{(kind is null ? " on" : "")}" href="{Link("type", null)}">all</a>""");
            sb.Append("</div>");
            if (pub is not null)
            {
                var publisherKinds = sources.SelectMany(r => r.CatalogueKinds(null))
                    .GroupBy(x => x.Kind).Select(g => (g.Key, g.Sum(x => x.Works)))
                    .OrderByDescending(x => x.Item2).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
                var activeClass = kind is null ? "Choose a source class" : $"{SourceClassLabel(kind)} ({kind})";
                sb.Append($"""<details class="facetgroup sourceclasses"><summary>{H(activeClass)} <span>{publisherKinds.Count:n0} classes</span></summary><div>""");
                foreach (var (k, n) in publisherKinds)
                    sb.Append($"""<a class="f{(kind == k ? " on" : "")}" href="{Link("type", k)}" title="{H(k)}">{H(SourceClassLabel(k))} <span class="mono raw">{H(k)}</span> <span class="n">{n:n0}</span></a>""");
                sb.Append("</div></details>");
            }
            if (pub is null)
            {
                sb.Append("""<div class="facetgroups" role="group" aria-label="Source classes by jurisdiction">""");
                foreach (var r in readers.Values.OrderBy(r => r.Collection, StringComparer.Ordinal))
                {
                    var kinds = r.CatalogueKinds(null).OrderByDescending(x => x.Works)
                        .ThenBy(x => x.Kind, StringComparer.Ordinal).ToList();
                    var jurisdiction = r.Stamp.GetValueOrDefault("jurisdiction", r.Collection);
                    sb.Append($"""<details class="facetgroup"><summary>{H(jurisdiction)} · {H(r.Stamp.GetValueOrDefault("publisher_name", r.Collection))} <span>{kinds.Count:n0} classes</span></summary><div>""");
                    foreach (var (k, n) in kinds)
                        sb.Append($"""<a class="f" href="{KindLink(r.Collection, k)}" title="{H(k)}">{H(SourceClassLabel(k))} <span class="mono raw">{H(k)}</span> <span class="n">{n:n0}</span></a>""");
                    sb.Append("</div></details>");
                }
                sb.Append("</div>");
            }

            sb.Append($"""
                <div><i>text</i>
                <a class="f{(text is null ? " on" : "")}" href="{Link("text", null)}">any coverage</a>
                <a class="f{(text == true ? " on" : "")}" href="{Link("text", "yes")}">some text held</a>
                <a class="f{(text == false ? " on" : "")}" href="{Link("text", "no")}">no text held</a></div>
                <div><i>sort</i>
                <a class="f{(order == CatalogueOrder.Name ? " on" : "")}" href="{Link("sort", null)}">name</a>
                <a class="f{(order == CatalogueOrder.MostVersions ? " on" : "")}" href="{Link("sort", "versions")}">most versions</a>
                <a class="f{(order == CatalogueOrder.MostRecent ? " on" : "")}" href="{Link("sort", "recent")}">most recent</a>
                <a class="f{(order == CatalogueOrder.Oldest ? " on" : "")}" href="{Link("sort", "oldest")}">oldest first</a></div>
                </nav>
                """);

            if (kind is "RECUEIL" or "CODE_RECUEIL")
                sb.Append("""
                    <div class="notice"><b>These are thematic collections, not single laws.</b>
                    Legilux uses each record as a shelf for member acts. Lex keeps the official timeline
                    and metadata, but does not turn a compilation PDF into invented provisions or citations.
                    Individual member acts remain searchable when the publisher exposes them as legal works.
                    <a href="/coverage">Read the coverage contract →</a></div>
                    """);

            if (rows.Count == 0)
            {
                sb.Append("""<div class="card"><p>No work matches those filters. <a href="/browse">Clear them</a>.</p></div>""");
            }
            else
            {
                sb.Append("""
                    <div class="card catcard"><table class="cat">
                    <tr><th>work</th><th>jurisdiction</th><th>source class</th><th class="r">dated versions</th><th>first</th><th>last</th><th>text coverage</th></tr>
                    """);
                foreach (var w in rows)
                {
                    var jurisdiction = readers.GetValueOrDefault(w.Collection)?.Stamp
                        .GetValueOrDefault("jurisdiction", w.Collection) ?? w.Collection;
                    var classLabel = SourceClassLabel(w.Kind);
                    var coverage = w.TextVersions == w.Versions
                        ? "full text"
                        : w.TextVersions > 0
                            ? $"partial text, {w.TextVersions:n0} of {w.Versions:n0} versions"
                            : IsThematicCollection(w.Kind) ? "collection metadata" : "record only";
                    var coverageTitle = w.TextVersions == w.Versions
                        ? $"Full publisher text is held for all {w.Versions:n0} dated versions."
                        : w.TextVersions > 0
                            ? $"Publisher text is held for {w.TextVersions:n0} of {w.Versions:n0} dated versions."
                        : IsThematicCollection(w.Kind)
                            ? "This is an official thematic collection; its compilation is not treated as one legal instrument."
                            : "Lex holds the publisher record, but no safely derived provision text for its dated versions.";
                    var publisherTitle = StripConsolidationLabel(w.TitleShort ?? w.Title);
                    var title = string.IsNullOrWhiteSpace(publisherTitle)
                        ? "Untitled publisher record" : publisherTitle;
                    sb.Append($"""
                        <tr>
                          <td><a href="/{H(w.Collection)}/{H(w.GroupKey)}">{H(title)}</a>
                              <div class="sub mono cat-desktop">{H(w.GroupKey)}</div>
                              <div class="sub cat-mobile">{H(jurisdiction)} · {H(classLabel)}{(string.IsNullOrEmpty(w.Kind) ? "" : $" ({H(w.Kind)})")} · {w.Versions:n0} dated version{(w.Versions == 1 ? "" : "s")}<br><span class="mono">{H(w.FirstFrom)} → {H(w.LastFrom)}</span></div></td>
                          <td><span class="badge">{H(jurisdiction)}</span></td>
                          <td>{H(classLabel)}{(string.IsNullOrEmpty(w.Kind) ? "" : $" <span class=\"sub mono\">{H(w.Kind)}</span>")}</td>
                          <td class="r">{w.Versions:n0}</td>
                          <td class="mono">{H(w.FirstFrom)}</td>
                          <td class="mono">{H(w.LastFrom)}</td>
                          <td><span class="badge {(w.TextVersions > 0 ? "ok" : "")}" title="{H(coverageTitle)}">{coverage}</span></td>
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
            sb.Append($"""
                <h2>Start here</h2>
                <ul>
                  <li><a href="/lu-legilux/rgd-1998-08-03-n4/2018-01-01">Nouveau Code de proc&#233;dure civile, as it stood on 1 Jan 2018</a></li>
                  <li><a href="/lu-legilux/code-environnement">Code de l'environnement, full timeline</a></li>
                  <li><a href="/lu-legilux/recueil-protection_donnees">Recueil protection des donn&#233;es, timeline</a></li>
                  <li><a href="/in-force-on?date=2022-03-15&amp;kind=CODE">Which codes were in force on 15 Mar 2022?</a></li>
                  <li><a href="/eu-eurlex/32013r0575">CRR (EU) 575/2013-22 consolidated versions, incl. future-dated</a></li>
                  <li><a href="/eu-eurlex/32016r0679/2019-01-01">GDPR, official consolidated wording selected for 1 Jan 2019</a></li>
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
                    <span class="badge">{c.Versions:n0} versions</span>
                    <span class="badge">{H(c.EarliestValidFrom)} &rarr; {H(c.LatestValidFrom)}</span>
                    <span class="badge {(r.SignatureValid ? "ok" : "warn")}">{(r.SignatureValid ? "signed index" : "unsigned")}</span>
                    <div class="sub" style="margin-top:6px">Mounted coverage: {H(c.EarliestValidFrom)} to {H(c.LatestValidFrom)}. Scope and known gaps are stated on the <a href="/coverage">coverage page</a>.</div>
                    </div>
                    """);
            }
            sb.Append($"""
                <h2>Look something up</h2>
                <form class="inline" action="/search"><input name="q" aria-label="Words to search in legal text" placeholder="words in the text, e.g. protection des donn&#233;es" style="flex:1;min-width:240px"><button>Search</button></form>
                <form class="inline" action="/go-asof">
                  <input name="work" aria-label="Work slug or Lex ID" placeholder="work slug or Lex ID" style="flex:1;min-width:200px">
                  <select name="publisher" aria-label="Jurisdiction or publisher">
                    <option value="">find it across every jurisdiction</option>
                    {string.Join("", readers.Values.OrderBy(r => r.Collection, StringComparer.Ordinal).Select(r => $"<option value=\"{H(r.Collection)}\">{H(r.Stamp.GetValueOrDefault("publisher_name", r.Collection))}</option>"))}
                  </select>
                  <input name="date" type="date" aria-label="Date on which to read the work" value="2022-03-15">
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
                ["name"] = "Lex: point-in-time Luxembourg and reviewed-scope EU law",
                ["description"] = "Official dated Luxembourg expressions and reviewed-scope EU law, "
                    + "including every available official consolidation in the mounted scope, with explicit publisher timeline semantics, "
                    + "per-article history, explicit coverage gaps, and a SHA-256 chain to the publisher's own bytes.",
                ["url"] = $"{ctx.PublicBase}/browse",
                // No license here either. This Dataset node is arguably Lex's own catalogue
                // metadata rather than the publishers' text, so a claim about it would be ours to
                // make. But the same literal sat in both places as though the distinction had
                // never come up, and it named a licence for a dataset whose whole content is
                // derived from publisher material under terms we have not established. Free to
                // access is a fact about this site and stays; a redistribution licence is not.
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
                sb.ToString(), "Every legal work and publisher collection, with source class, dated versions and exact text coverage.",
                "browse", canonicalPath: "/browse", jsonLd: datasetLd), "text/html");
        });

        app.MapGet("/go-asof", (string work, string date, string? publisher) =>
        {
            var value = work.Trim();
            var colon = value.IndexOf(':');
            if (colon > 0 && readers.ContainsKey(value[..colon]))
            {
                publisher = value[..colon];
                value = value[(colon + 1)..];
            }
            if (!string.IsNullOrWhiteSpace(publisher) && readers.ContainsKey(publisher))
                return Results.Redirect($"/{Uri.EscapeDataString(publisher)}/{Uri.EscapeDataString(value)}/{Uri.EscapeDataString(date)}");

            // A bare slug is not globally unique. Let the mixed-corpus identifier search resolve
            // it rather than silently treating every future jurisdiction as Luxembourg.
            return Results.Redirect("/?space=search&q=" + Uri.EscapeDataString(value)
                + "&asOf=" + Uri.EscapeDataString(date));
        });

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
                sb.Append("<div class=\"card\"><table tabindex=\"0\" aria-label=\"Text coverage by document type\"><tr><th>document type</th><th>versions</th>"
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
                sb.Append("</table></div>");
                // The confidence mix, as a count rather than a claim. Text from publisher markup and text
                // read out of a PDF are different evidence, and the page that reports coverage is the page
                // that should say how much of it is which.
                if (c.Profiles.Count > 0)
                {
                    sb.Append("<div class=\"card\"><table tabindex=\"0\" aria-label=\"Extraction confidence by profile\"><tr><th>how the text was obtained</th><th>versions</th></tr>");
                    foreach (var pr in c.Profiles)
                    {
                        var what = pr.Profile switch
                        {
                            "akn-lu/1" => "publisher XML (Akoma Ntoso), article boundaries from the publisher",
                            "akn-lu/2" => "publisher XML (Akoma Ntoso), article boundaries from the publisher; publisher-only structural placeholders are preserved as non-searchable coverage evidence",
                            "akn-lu-identical-scl-duplicate/1" => "publisher XML (Akoma Ntoso), after one disclosed byte-identical presentation-attribute repair for parsing",
                            "akn-lu-document/1" => "publisher XML (Akoma Ntoso), exposed as one document because the publisher supplied no article or annex boundary",
                            "fmx4-eu/1" => "publisher XML (Formex 4), article boundaries from the publisher",
                            "xhtml-eu/1" => "publisher XHTML, article boundaries from the publisher",
                            "xhtml-eu-xlink-context/1" => "legacy publisher XHTML with its missing standard link namespace supplied; article boundaries from publisher markup, or one disclosed document-level boundary when none exists",
                            "html-eu-tolerant/1" => "legacy publisher HTML repaired deterministically; article boundaries from publisher markup, or one disclosed document-level boundary when none exists",
                            "pdf-lu/1" => "read from the publisher's PDF, article boundaries inferred from layout",
                            "pdf-memorial-lu/1" => "cut out of an official gazette issue, both the act's boundaries and its articles inferred",
                            "pdf-memorial-lu/2" => "cut out of a verified official-gazette section; older article typography normalized, or one disclosed document boundary when the section has no unambiguous single article sequence",
                            _ => "",
                        };
                        sb.Append($"<tr><td class=\"mono\">{H(pr.Profile)}</td><td>{pr.Versions:n0}</td></tr>"
                                + (what.Length > 0 ? $"<tr><td colspan=\"2\" class=\"sub\">{what}</td></tr>" : ""));
                    }
                    sb.Append("</table></div>");
                }
                sb.Append(EnvelopeCard(r, false));
                var jurisdiction = r.Stamp.GetValueOrDefault("jurisdiction", "").ToUpperInvariant();
                var luGap = jurisdiction switch
                {
                    "LU" => """
                      The publisher only maintains consolidated (amendments-merged) editions for some laws,
                      the codes and frequently amended acts. Lex holds <b>all of those</b>. The other
                      Luxembourg acts never get a consolidated edition; they are <b>not here yet</b>
                      (and we won't guess dates for texts we haven't seen).
                      """,
                    "EU" => $" The mounted index contains {c.Groups:n0} EU acts and related legal materials from the reviewed scope. Expansion remains gated by the scope preview and corpus release.",
                    _ => $" The mounted index contains {c.Groups:n0} works from this publisher's configured scope. See its release manifest for exact inclusion rules.",
                };
                // Measured against the publisher's own catalogue, 2026-08-04. The cause is the file format
                // offered per version, not our pipeline and not the age of the act, and it lands mostly on
                // documents that are not instruments at all.
                var gapWhy = jurisdiction == "LU"
                    ? """
                      <b>Why:</b> Lex ingests the publisher's XML, because XML is the only format that marks
                      where each article begins and ends, which is what makes an article citable, hashable and
                      comparable across dates. Legilux offers XML for 2,892 of its consolidations, PDF only for
                      1,611, and no file at all for 130. Roughly 1,371 of those PDF-only versions are the
                      thematic folders marked above, which nobody voted and which carry no rule of their own.
                      The live rows above are the current result, not a copied estimate. The remaining wordless
                      records are publisher collections with no single authoritative instrument text, fileless
                      catalogue records, or a source Lex refused because the requested act's identity could not
                      be proved inside the linked gazette issue. Each record still links to the publisher.
                      """
                    : "";
                sb.Append($"""
                    <div class="notice"><b>What we hold, and what we honestly don't.</b>
                    {c.Groups:n0} publisher works and collections in {c.Versions:n0} dated snapshots.{luGap}
                    Of those snapshots, <b>{c.VersionsWithText:n0}</b> carry the full official text and
                    <b>{c.Versions - c.VersionsWithText:n0}</b> are a dated entry with its source and hash but no wording.
                    {gapWhy}
                    Those answer with <span class="mono">text_not_available</span> rather than pretending.
                    History can never go deeper than what the publisher itself digitised.</div>
                    """);
            }
            return Results.Content(Page("Coverage, what we hold, and what we lack", sb.ToString(),
                canonicalPath: "/coverage",
                description: "Live publisher coverage, text availability, extraction profiles and explicit legal-data gaps in Lex.",
                assistant: false), "text/html");
        });

        app.MapGet("/in-force-on", (string? date, string? publisher, string? kind, int? page) =>
        {
            var sb = new StringBuilder();
            var kindOptions = string.Join("", readers.Values.SelectMany(r => r.Coverage().Kinds)
                .Where(k => k.Kind is not null)
                .GroupBy(k => k.Kind, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .OrderBy(k => k.Kind, StringComparer.OrdinalIgnoreCase)
                .Select(k => $"<option {(k.Kind == kind ? "selected" : "")}>{H(k.Kind)}</option>"));
            var publisherOptions = string.Join("", readers.Values.OrderBy(r => r.Collection, StringComparer.Ordinal)
                .Select(r => $"<option value=\"{H(r.Collection)}\"{(publisher == r.Collection ? " selected" : "")}>{H(r.Stamp.GetValueOrDefault("publisher_name", r.Collection))}</option>"));
            sb.Append($"""
                <form class="inline">
                  <input type="date" name="date" aria-label="Date whose publisher states to list" value="{H(date ?? "2022-03-15")}">
                  <select name="publisher" aria-label="Jurisdiction or publisher"><option value="">Every jurisdiction</option>{publisherOptions}</select>
                  <select name="kind" aria-label="Source class"><option value="">any source class</option>{kindOptions}</select>
                  <button>Show</button>
                </form>
                """);
            if (TryIsoDate(date, out var d))
            {
                var p = Math.Max(0, (page ?? 1) - 1);
                const int limit = 50;
                foreach (var r in readers.Values.Where(r => publisher is null || r.Collection == publisher))
                {
                    var inForcePage = r.InForceOn(d, new FilterSet(null, null, string.IsNullOrEmpty(kind) ? null : kind, null), limit, p * limit);
                    var rows = inForcePage.Rows;
                    var total = inForcePage.TotalGroups;
                    var publisherVersionDates = UsesPublisherVersionDates(r);
                    var populationLabel = publisherVersionDates
                        ? "works with an official consolidated wording state covering"
                        : "works applicable on";
                    sb.Append($"<h2>{H(r.Stamp.GetValueOrDefault("publisher_name"))}, {total:n0} {populationLabel} {d:yyyy-MM-dd}</h2>");
                    if (inForcePage.Ambiguities.Count > 0)
                        sb.Append($"<div class=\"notice\"><b>ambiguous_version.</b> {inForcePage.Ambiguities.Count:n0} work(s) have multiple publisher states on this boundary; choose an exact state below.</div>");
                    sb.Append($"<div class=\"card\"><table><tr><th>work</th><th>type</th><th>{(publisherVersionDates ? "publisher wording state" : "applicability interval")}</th></tr>");
                    foreach (var row in rows)
                        sb.Append($"""
                            <tr><td><a href="/{H(row.Collection)}/{H(row.GroupKey)}/{d:yyyy-MM-dd}">{H(DocTitle(row))}</a></td>
                            <td><span class="badge">{H(row.Kind)}</span></td><td class="mono">{IntervalLabel(r, row)}</td></tr>
                            """);
                    foreach (var ambiguity in inForcePage.Ambiguities)
                        foreach (var choice in ambiguity.Choices.Take(20))
                            sb.Append($"""
                                <tr><td><a href="/{H(choice.Collection)}/{H(choice.GroupKey)}/{H(VersionCoordinate(choice))}">{H(DocTitle(choice))}</a>
                                <span class="sub">exact publisher state</span></td>
                                <td><span class="badge">{H(choice.Kind)}</span></td><td class="mono">{IntervalLabel(r, choice)}</td></tr>
                                """);
                    sb.Append("</table></div>");
                    if (total > limit)
                    {
                        sb.Append("<p>");
                        // Query-string positions need percent-encoding, not the HTML encoder.
                        // H() neutralises the characters that matter for markup and leaves the
                        // ones that matter for a URL, so a publisher or kind containing an
                        // ampersand or a hash silently rewrote the rest of the link. Every other
                        // link builder on this page already used EscapeDataString.
                        var scope = $"&amp;publisher={Uri.EscapeDataString(publisher ?? "")}"
                            + $"&amp;kind={Uri.EscapeDataString(kind ?? "")}";
                        if (p > 0) sb.Append($"<a href=\"?date={d:yyyy-MM-dd}{scope}&amp;page={p}\">← previous</a> &nbsp;");
                        if ((p + 1) * limit < total) sb.Append($"<a href=\"?date={d:yyyy-MM-dd}{scope}&amp;page={p + 2}\">next →</a>");
                        sb.Append($" <span class=\"sub\">page {p + 1} of {(total + limit - 1) / limit}</span></p>");
                    }
                    var gap = r.Stamp.GetValueOrDefault("jurisdiction", "").ToUpperInvariant() switch
                    {
                        // No count. The count-at-build rule forbids a population literal in copy,
                        // and this one is measurably wrong: the gap matrix puts the
                        // never-consolidated set at 23,370 of a 24,622 population, not 24,579. It
                        // cannot be computed here either, because those acts are precisely the ones
                        // not ingested, so the honest move is to state the class and not size it.
                        "LU" => "Never-consolidated Luxembourg acts are not ingested (count and date coverage unmeasured).",
                        "EU" => "EU coverage is the reviewed configured scope, not the complete EUR-Lex universe.",
                        _ => "Coverage is limited to this publisher's configured and verified scope.",
                    };
                    sb.Append($"""
                        <div class="notice"><b>Population disclosure.</b> Basis: versioned works only ({r.Coverage().Groups:n0} works).
                        {H(gap)} See <a href="/coverage">coverage</a>.</div>
                        """);
                    sb.Append(EnvelopeCard(r, IsProvisional(r, d)));
                }
            }
            return Results.Content(Page("Publisher state on a date", sb.ToString(),
                "Luxembourg rows describe applicability; EU rows describe official consolidated wording states, not entry into force.",
                extraHead: NoIndexFollow), "text/html");
        });

        app.MapGet("/search", (string? q, string? kind, string? publisher) =>
        {
            var sb = new StringBuilder();
            var publisherOptions = string.Join("", readers.Values.OrderBy(r => r.Collection, StringComparer.Ordinal)
                .Select(r => $"<option value=\"{H(r.Collection)}\"{(publisher == r.Collection ? " selected" : "")}>{H(r.Stamp.GetValueOrDefault("publisher_name", r.Collection))}</option>"));
            var kindOptions = string.Join("", readers.Values.SelectMany(r => r.Coverage().Kinds)
                .Where(k => k.Kind is not null).GroupBy(k => k.Kind, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First()).OrderBy(k => k.Kind, StringComparer.OrdinalIgnoreCase)
                .Select(k => $"<option{(k.Kind == kind ? " selected" : "")}>{H(k.Kind)}</option>"));
            sb.Append($"""
                <form class="inline"><input name="q" value="{H(q)}" aria-label="Words to search in legal text" placeholder="search article text &amp; titles" style="flex:1;min-width:240px">
                <select name="publisher" aria-label="Jurisdiction or publisher"><option value="">Every jurisdiction</option>{publisherOptions}</select>
                <select name="kind" aria-label="Source class"><option value="">any source class</option>{kindOptions}</select>
                <button>Search</button></form>
                <p class="sub">Work and article search over the same current retrieval path as the workspace. Filters run before ranking, always.</p>
                """);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var searchArguments = new JsonObject
                {
                    ["query"] = q,
                    ["limit"] = 15,
                    ["time_scope"] = "as_of",
                    ["as_of"] = ctx.Today.ToString("yyyy-MM-dd"),
                    ["retrieval_mode"] = "keyword",
                    ["fuzzy"] = "auto",
                };
                if (!string.IsNullOrEmpty(publisher)) searchArguments["publisher"] = publisher;
                if (!string.IsNullOrEmpty(kind)) searchArguments["document_type"] = kind;
                JsonArray envelopes;
                try
                {
                    // CallTool returns a JsonNode. A per-publisher answer is an array, but a
                    // WHOLE-CALL refusal is a bare object: unknown_publisher and
                    // no_corpus_mounted both arrive that way. The old cast fell back to an
                    // empty array, the loop never ran, and the page rendered the form and
                    // nothing else. No count, no notice, no explanation: a reader who typed a
                    // publisher that is not mounted was shown a blank result area and left to
                    // conclude whatever they liked.
                    var answer = mcpCore.CallTool("search", searchArguments);
                    if (answer is JsonObject refusal)
                    {
                        sb.Append(TrustNotices.WholeCallRefusal(refusal));
                        return Results.Content(Page("Search", sb.ToString(), extraHead: NoIndexFollow),
                                               "text/html");
                    }
                    // Neither the per-publisher array nor the whole-call object. An answer of a
                    // shape this page does not know became an empty array, the loop never ran, and
                    // the reader got the form above nothing, which is the blank page this module
                    // exists to prevent.
                    if (answer is not JsonArray array)
                    {
                        sb.Append(TrustNotices.UnreadableResults());
                        return Results.Content(Page("Search", sb.ToString(), extraHead: NoIndexFollow),
                                               "text/html");
                    }
                    envelopes = array;
                }
                catch (ArgumentException error)
                {
                    return Results.Content(Page("Bad search query",
                        $"<div class=\"notice\">status <span class=\"mono\">invalid_request</span>, "
                        + $"{H(error.Message)}</div>", extraHead: NoIndexFollow), "text/html", statusCode: 400);
                }
                sb.Append(RenderSearchResults(envelopes, readers));
            }
            return Results.Content(Page("Search", sb.ToString(), extraHead: NoIndexFollow), "text/html");
        });

        // ---- /changed: the corpus-wide counterpart of a diff. "What moved between two dates?"
        // is the question a compliance reader actually has, and no per-work tool can answer it.
        app.MapGet("/changed", (string? from, string? to, string? order, string? publisher) =>
        {
            var today = ctx.Today;
            if (!TryIsoDate(to, out var toD)) toD = today;
            if (!TryIsoDate(from, out var fromD)) fromD = toD.AddYears(-1);
            if (fromD > toD) (fromD, toD) = (toD, fromD);
            var byChurn = order == "by_churn";
            var f = fromD.ToString("yyyy-MM-dd");
            var t = toD.ToString("yyyy-MM-dd");
            var publisherOptions = string.Join("", readers.Values.OrderBy(r => r.Collection, StringComparer.Ordinal)
                .Select(r => $"<option value=\"{H(r.Collection)}\"{(publisher == r.Collection ? " selected" : "")}>{H(r.Stamp.GetValueOrDefault("publisher_name", r.Collection))}</option>"));

            var sb = new StringBuilder($"""
                <p class="lede">Every law in Lex that gained a new version between two
                dates, the corpus-wide view that a single law's timeline cannot give you.</p>
                <form class="inline" method="get">
                  <label class="sub">from <input type="date" name="from" value="{f}"></label>
                  <label class="sub">to <input type="date" name="to" value="{t}"></label>
                  <select name="publisher" aria-label="Jurisdiction or publisher"><option value="">Every jurisdiction</option>{publisherOptions}</select>
                  <select name="order" aria-label="Change ranking">
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

            // One resolved set, then every number and every notice derives from it. Previously
            // the filter accepted null only, so an absent publisher and an empty one selected
            // different sets from the same form, and an unrecognised value such as LU selected
            // no reader at all while still being handed to the caveat, which then reported a
            // Luxembourg observation about a set that never ran.
            var selected = readers.Values
                .Where(x => string.IsNullOrEmpty(publisher) || x.Collection == publisher)
                .OrderBy(x => x.Collection, StringComparer.Ordinal)
                .ToList();
            var totalWorks = 0; var totalVersions = 0;
            var blocks = new StringBuilder();
            foreach (var r in selected)
            {
                var (works, versions) = r.ChangeTotals(f, t, null);
                totalWorks += works; totalVersions += versions;
                var rows = r.ChangesInPeriod(f, t, null, byChurn, 60);
                if (rows.Count == 0) continue;
                blocks.Append($"<h2>{H(r.Collection)}, {works:n0} law(s) moved, {versions:n0} new version(s)</h2>");
                blocks.Append("<div class=\"card\"><table><tr><th>law</th><th>new versions</th><th>window</th><th></th></tr>");
                foreach (var c in rows)
                {
                    var diffFrom = c.Baseline ?? c.FirstChange;
                    var canCompare = diffFrom != c.LastChange && c.DistinctTexts > 1;
                    blocks.Append($"""
                        <tr><td><a href="/{H(r.Collection)}/{H(c.GroupKey)}">{H(TitleShorten(c.Title) ?? c.GroupKey)}</a>
                            <div class="sub mono" style="font-size:12px">{H(r.Stamp.GetValueOrDefault("jurisdiction", r.Collection))} · {H(c.GroupKey)} · {c.VersionsTotal} version(s) in all</div></td>
                        <td class="mono">{c.VersionsInPeriod}</td>
                        <td class="mono">{H(c.FirstChange)}{(c.FirstChange == c.LastChange ? "" : " → " + H(c.LastChange))}</td>
                        <td>{(!canCompare
                                ? $"<a href=\"/{H(r.Collection)}/{H(c.GroupKey)}/{H(c.LastChange)}\">read</a>"
                                : $"<a href=\"/{H(r.Collection)}/{H(c.GroupKey)}/diff/{H(diffFrom)}/{H(c.LastChange)}\">what changed</a>")}</td></tr>
                        """);
                }
                blocks.Append("</table></div>");
            }

            sb.Append($"""
                <div class="card" style="border-color:var(--accent)">
                  <b>{totalWorks:n0} held work(s) changed</b> between {H(f)} and {H(t)},
                  producing <b>{totalVersions:n0} new held state(s)</b>.
                  {(totalWorks == 0 ? "That is what Lex observed in held states, not a finding "
                      + "that no law changed." : "")}
                </div>
                """);
            // The fifth Phase 0 notice. It shipped in the browser bundle and never here, on the
            // one page in this lane that states a change count.
            sb.Append(TrustNotices.HistoricalDensity(f, selected));
            sb.Append(blocks);
            sb.Append($"""
                <p class="sub">Same data, from your own code:
                <span class="mono">changes_in_period(from_date="{H(f)}", to_date="{H(t)}"{(byChurn ? ", order=\"by_churn\"" : "")})</span>
                ,  <a href="/developers">try it in the browser</a>.</p>
                """);
            return Results.Content(Page("What changed", sb.ToString(), null, "find",
                extraHead: NoIndexFollow), "text/html");
        });

        // ---- /find: one door for the three ways of locating a law (search, browse, in-force-on).
        app.MapGet("/find", () =>
        {
            var body = $"""
                <p class="lede">Three ways in. All of them end at the same place: exact publisher wording
                selected on a date you choose, with the publisher's time semantics made explicit.</p>

                <div class="card"><h2 style="margin-top:0">Search by words</h2>
                <p class="sub">Finds the individual article, not just the law, search runs over every provision.</p>
                <form class="inline plainsearch" action="/search" method="get">
                  <input name="q" aria-label="Words to search for in the text" style="flex:1;min-width:240px" placeholder="e.g. congé parental, breach notification, own funds">
                  <input type="date" name="as_of" aria-label="Only publisher states covering this date">
                  <button type="submit">Search</button>
                </form></div>

                <div class="card"><h2 style="margin-top:0">What did each publisher record on a date?</h2>
                <p class="sub">Luxembourg results describe applicability. EU results identify official consolidated wording states and do not claim entry into force.</p>
                <form class="inline" action="/in-force-on" method="get">
                  <input type="date" name="date" aria-label="Date whose publisher states to list" value="{ctx.Today:yyyy-MM-dd}">
                  <button type="submit">List it</button>
                </form></div>

                <div class="card"><h2 style="margin-top:0">What changed between two dates?</h2>
                <p class="sub">Across the whole corpus, not one law at a time, the question a compliance
                reader actually has.</p>
                <p><a href="/changed"><b>Open the change report →</b></a> ·
                   <a href="/changed?from=2020-03-01&amp;to=2021-07-01&amp;order=by_churn">the pandemic, ranked by churn</a></p></div>

                <div class="card"><h2 style="margin-top:0">Browse everything</h2>
                <p class="sub">All {readers.Values.Sum(r => r.Coverage().Groups):n0} publisher works and collections, by source and type.</p>
                <p><a href="/browse"><b>Open the catalogue →</b></a></p></div>

                <p class="sub">Not sure where to start? The <a href="/">assistant</a> takes a plain question,
                or read <a href="/stories">four laws with a story</a>.</p>
                """;
            return Results.Content(Page("Find a law", body, null, "find",
                canonicalPath: "/find",
                description: "Search legal text, inspect publisher states on a date, compare change windows or browse the complete Lex catalogue."), "text/html");
        });

        return app;
    }

    /// <summary>
    /// The result area for one search: what each publisher contributed, and then whatever absence
    /// the page may honestly state once they have all answered.
    ///
    /// Separated from the route so the mixed states can be constructed directly, because they
    /// cannot be reached through the fixture. It mounts one publisher, and the executed search path
    /// always stamps ok with query_ran true, so no page-level test can produce a publisher that ran
    /// beside one that refused. That is exactly the state where the absence rule went wrong.
    /// </summary>
    public static string RenderSearchResults(
        JsonArray envelopes, IReadOnlyDictionary<string, LexIndexReader> readers)
    {
        var sb = new StringBuilder();
        var ran = 0;
        var refused = 0;
        // Everything the page put in front of the reader AS A MATCH, which is not the same as the
        // wording hits it rendered as text. Counting only wording hits left the corpus-wide absence
        // sentence free to say no match was returned directly underneath a card naming the records
        // that had just matched. A count is a claim, and so is a zero.
        var presented = 0;
        // Answers this page could not classify. Counted, never guessed at.
        var unreadable = 0;

        /// <summary>
        /// Whether this publisher's results can be classified at all.
        ///
        /// Absent hits is a real empty result and stays one. Hits that are present but are not
        /// an array, an element that is not an object, or match_reasons present but not an
        /// array, are none of them empty results: they are answers the page cannot read. The
        /// old reads collapsed every one of them into an empty array, and an empty array here
        /// becomes a corpus-wide claim that nothing matched.
        /// </summary>
        static bool Classifiable(JsonObject result, out JsonArray hits)
        {
            hits = [];
            if (result["hits"] is { } raw)
            {
                if (raw is not JsonArray array) return false;
                hits = array;
            }
            foreach (var hit in hits)
            {
                if (hit is not JsonObject entry) return false;
                if (entry["match_reasons"] is not { } reasons) continue;
                if (reasons is not JsonArray listed) return false;
                // An array of the wrong things is not an array of reasons. Checking only the
                // container let [9,true] through as "no wording reason", which is a reading of
                // the response rather than a fact about it: the page then labelled a valid work
                // as matched on its title, which the response never said.
                foreach (var reason in listed)
                    if (TrustNotices.Text(reason) is null) return false;
            }
            return true;
        }

        static string Heading(LexIndexReader reader, string suffix = "") =>
            $"<h2>{H(reader.Stamp.GetValueOrDefault("publisher_name"))} "
            + $"({H(reader.Stamp.GetValueOrDefault("jurisdiction"))}){suffix}</h2>";

        foreach (var node in envelopes)
        {
            // A sibling of the wrong shape was silently erased by OfType, and erasing it is how
            // it became an absence: beside a refusal the page went on to say no selected
            // publisher ran this query, about a response it had thrown away unread.
            if (node is not JsonObject result)
            {
                sb.Append(TrustNotices.UnreadableResults());
                unreadable++;
                continue;
            }
            // Silently skipping an envelope makes a whole publisher's results vanish from a page
            // that gives the reader no way to know it answered. Absence is never implied here, so
            // an envelope this page cannot attribute is disclosed rather than dropped. The read is
            // also strict: the previous GetValue threw on a non-string publisher and took the whole
            // page with it.
            if (!TryAttribute(result, readers, out var reader))
            {
                sb.Append("<div class=\"notice\" role=\"note\">A publisher answered and its "
                    + "results could not be attributed to a mounted index, so they are not "
                    + "shown. This is not evidence that it found nothing.</div>");
                continue;
            }
            var publisherId = reader.Collection;
            // Classify before touching hits, and fail closed. Reading hits first let a missing or
            // malformed status, an ok envelope carrying query_ran false, or a refusal arriving with
            // hostile rows, all render results or a count for a query nobody executed. Only an
            // exact ok whose own receipt confirms it counts as a run; everything else states what
            // is known and shows nothing.
            var status = TrustNotices.EnvelopeStatus(result);
            if (!TrustNotices.Ran(result))
            {
                sb.Append(Heading(reader));
                sb.Append(TrustNotices.SearchEnvelopeRefusal(status ?? "unusable_result", result));
                refused++;
                continue;
            }
            // Classify before reading, and disclose rather than guess. This publisher ran, so
            // the run is counted either way; what it returned is what could not be read.
            ran++;
            if (!Classifiable(result, out var hits))
            {
                sb.Append(Heading(reader));
                sb.Append(TrustNotices.UnreadableResults());
                unreadable++;
                continue;
            }
            // A hit that reached no wording matched the record, not the law. The assistant path
            // already drops these as filler (AskService); the web page presented them as answers,
            // which is how a speeding question returns tachograph regulations under status ok.
            //
            // The discriminator is the absence of a wording reason, not the presence of a label.
            // Only the identifier/title FALLBACK stamps match=work_identifier_or_title; a work-level
            // metadata hit from the main path arrives unlabelled, carrying work_metadata from the
            // FTS over identifiers, aliases, titles and facets (WorkSearch). Keying on the label
            // alone would miss exactly the hits attack 41 proved live.
            //
            // Every field below is read strictly. GetValue<string> THROWS on a number or a bool, so
            // one malformed field in one hit from any publisher took the entire page down with a
            // 500. A value of the wrong type is not the string, and it is not a page failure.
            static IEnumerable<string?> Reasons(JsonObject hit) =>
                (hit["match_reasons"] as JsonArray ?? []).Select(TrustNotices.Text);

            // The lanes that reached the provision's own text. fuzzy belongs here and was
            // missing: it is the SAME provision search re-run over a token-expanded query
            // (IndexReader.SearchV3), and the assistant path counts it too
            // (AskService.HasDirectProvisionEvidence). Leaving it out hid real text answers when
            // they were a publisher's only hits, and badged the rest as title matches, which was
            // a false statement about a match on the wording.
            static bool ReachedWording(JsonObject hit) =>
                Reasons(hit).Any(reason => reason is "keyword" or "fuzzy" or "semantic");

            // A real article row selected by its NUMBER rather than by its text, with an anchor
            // and provision text of its own. It is neither a wording match nor a record match,
            // and treating it as the latter was wrong twice over: a bare "Article 14" query
            // produces only these, so every hit was suppressed and the page reported that nothing
            // was presented, while the badge said it matched a title it had never looked at.
            static bool MatchedArticleNumber(JsonObject hit) =>
                Reasons(hit).Any(reason => reason is "article_intent");

            // The identity lanes: the whole query IS the instrument's identifier, title or
            // publisher short title, or contains one as a whole-word span. That is the reader
            // naming the law they want, and answering it with "records that match only in
            // metadata. They are not shown as text answers" is both unhelpful and untrue about a
            // precise identification. They answer a different question from a wording query;
            // they do not fail to answer.
            //
            // Listed explicitly rather than matched by prefix, because the vocabulary is closed
            // and anything new should fall to the suppressed side. The ambiguous_ variants are
            // deliberately absent: those are identity matches that resolved to more than one
            // work, so presenting one as the instrument would be a guess. exact_alias and
            // contained_alias are unreachable today, since work_names only stores identifier,
            // title and publisher_short_title kinds; they are named so the set stays complete if
            // that changes.
            static bool IdentifiedTheInstrument(JsonObject hit) => Reasons(hit).Any(reason =>
                reason is "exact_identifier" or "exact_title" or "exact_publisher_short_title"
                or "exact_alias" or "contained_identifier" or "contained_title"
                or "contained_publisher_short_title" or "contained_alias");

            // The identifier/title fallback rows carry no match_reasons at all, so the reason
            // test already excludes them and the old match=="work_identifier_or_title" clause was
            // redundant. It is gone rather than kept: if such a row ever did arrive carrying a
            // wording reason, that clause would have suppressed a real answer.
            static bool IsAnswer(JsonObject hit) =>
                ReachedWording(hit) || MatchedArticleNumber(hit) || IdentifiedTheInstrument(hit);

            var shown = hits.OfType<JsonObject>().Where(IsAnswer).ToList();
            var metadataOnly = hits.OfType<JsonObject>()
                .Where(hit => !IsAnswer(hit)).ToList();
            // Only when the noise would BE the answer. Alongside real text hits a record match is
            // context, and it keeps its badge below them.
            if (shown.Count == 0 && metadataOnly.Count > 0)
            {
                var works = metadataOnly.Select(hit => TrustNotices.Text(hit["work"]) ?? "")
                    .Where(work => work.Length > 0).ToList();
                // The card renders only when there is a record to name, so the heading waits for
                // it. A heading with nothing under it announced a publisher and then said nothing.
                if (TrustNotices.MetadataOnly(reader, works) is { } card)
                {
                    sb.Append(Heading(reader));
                    sb.Append(card);
                    presented += works.Count;
                    continue;
                }
                // Records matched and not one of them could be named, so this publisher
                // returned something and the page can show none of it. That is unreadable, not
                // empty, and it may not close the page with a no-match sentence.
                sb.Append(Heading(reader));
                sb.Append(TrustNotices.UnreadableResults());
                unreadable++;
                continue;
            }
            presented += shown.Count;
            sb.Append(Heading(reader, $", {shown.Count} hit(s)"));
            foreach (var hit in shown.Concat(metadataOnly))
            {
                // Four states, not two. Each badge names what was actually matched, because
                // "matched on title, not wording" was said about article numbers and about
                // identifiers alike, and it was true of neither.
                var badge = ReachedWording(hit)
                    ? ""
                    : MatchedArticleNumber(hit)
                        ? " <span class=\"badge\">matched on article number, not wording</span>"
                        : IdentifiedTheInstrument(hit)
                            ? " <span class=\"badge\">matched the name of this law, not its wording</span>"
                            : " <span class=\"badge\">matched on title, not wording</span>";
                var work = TrustNotices.Text(hit["work"]) ?? "";
                var validFrom = TrustNotices.Text(hit["valid_from"]) ?? "";
                var validTo = TrustNotices.Text(hit["valid_to"]);
                var anchor = TrustNotices.Text(hit["anchor"]);
                var title = TrustNotices.Text(hit["title"]) ?? work;
                var sourceClass = TrustNotices.Text(hit["document_type"]);
                var snippet = TrustNotices.Text(hit["snippet"]) ?? "";
                var provisionId = TrustNotices.Text(hit["provision_id"]) ?? work;
                var provisionLabel = TrustNotices.Text(hit["provision_num"])
                    ?? TrustNotices.Text(hit["provision_heading"]) ?? anchor;
                var href = $"/{H(publisherId)}/{H(work)}/{H(validFrom)}"
                    + (anchor is null ? "" : $"#{H(anchor)}");
                sb.Append($"""
                    <div class="card"><a href="{href}"><b>{H(title)}</b>{(provisionLabel is null ? "" : $",  {H(provisionLabel)}")}</a>
                    <span class="badge">{H(sourceClass)}</span> <span class="badge mono">{H(validFrom)} → {H(validTo ?? "open")}</span>{badge}
                    <div class="snippet">{H(snippet)}</div>
                    <div class="mono sub">{H(provisionId)}</div></div>
                    """);
            }
        }
        // Once every publisher has answered or refused, the page may only state a corpus-wide
        // absence if the corpus was actually searched and nothing was put in front of the reader.
        sb.Append(TrustNotices.SearchAbsence(ran, refused, presented, unreadable));
        return sb.ToString();
    }


    /// <summary>
    /// The mounted reader a search envelope belongs to, or false when it belongs to none.
    ///
    /// Separated from the page so the hostile cases are testable: the envelope is MCP output and
    /// therefore untrusted, and both failure directions were live here. A non-string publisher
    /// threw out of GetValue and took the entire search page with it; an absent one became the
    /// empty string, missed the registry, and dropped that publisher's hits with no trace on the
    /// page at all. The second is the worse of the two: a reader cannot see results that were
    /// never rendered, so a partial answer reads as a complete one.
    /// </summary>
    public static bool TryAttribute(
        JsonObject result,
        IReadOnlyDictionary<string, LexIndexReader> readers,
        [NotNullWhen(true)] out LexIndexReader? reader)
    {
        reader = null;
        // Every hop is checked, including that `envelope` is an object at all: indexing a
        // JsonValue with a property name throws, which the hostile test found immediately.
        return result["envelope"] is JsonObject envelope
            && envelope["publisher"] is JsonValue value
            && value.TryGetValue<string>(out var publisher)
            && publisher.Length > 0
            && readers.TryGetValue(publisher, out reader);
    }
}