using System.Text;
using System.Text.Json.Nodes;
using Lex.Index;
using static Lex.Web.PageShell;
using static Lex.Web.Fragments;

namespace Lex.Web;

/// <summary>
/// The front page. It renders the thesis, the counts and the suggested starting points, then mounts the workspace bundle; everything interactive above is client-side from there.
/// </summary>
public static class HomeEndpoints
{
    public static IEndpointRouteBuilder MapHome(this IEndpointRouteBuilder app, WebContext ctx)
    {
        var readers = ctx.Registry.All;
        var publicBase = ctx.PublicBase;
        var mcpCore = ctx.Mcp;
        string Page(string title, string body, string? subtitle = null, string nav = "",
                    string? h1 = null, string? canonicalPath = null, string? jsonLd = null,
                    string? description = null, string? lang = null, bool assistant = true)
            => PageShell.Page(ctx.PublicBase, title, body, subtitle, nav, h1, canonicalPath,
                              jsonLd, description, lang, ctx.Options.CodeCommit, assistant);

        app.MapGet("/", () =>
        {
            // Counts come from the mounted indexes at render time, never hand-written numbers.
            var cov = readers.Values.Select(r => r.Coverage()).ToList();
            var assetVersion = Uri.EscapeDataString(ctx.Options.CodeCommit ?? "dev");
            var tools = mcpCore.ToolDefs().OfType<JsonObject>()
                               .Select(t => t["name"]!.GetValue<string>()).ToList();
            var facetSets = readers.Values.Select(r => r.SearchFacets()).ToList();
            JsonArray Values(IEnumerable<string?> values) => new(values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .Select(value => (JsonNode)value!).ToArray());
            var searchFacets = new JsonObject
            {
                ["jurisdictions"] = new JsonArray(readers.Values
                    .Where(r => !string.IsNullOrWhiteSpace(r.Stamp.GetValueOrDefault("jurisdiction")))
                    .OrderBy(r => r.Stamp.GetValueOrDefault("jurisdiction"), StringComparer.OrdinalIgnoreCase)
                    .Select(r =>
                    {
                        var value = r.Stamp.GetValueOrDefault("jurisdiction")!;
                        var facets = r.SearchFacets();
                        var coverage = r.Coverage();
                        return (JsonNode)new JsonObject
                        {
                            ["value"] = value.ToLowerInvariant(),
                            ["code"] = value.ToUpperInvariant(),
                            ["publisher"] = r.Collection,
                            ["hierarchies"] = Values(facets.Hierarchies),
                            ["domains"] = Values(facets.Domains),
                            ["source_classes"] = Values(coverage.Kinds.Select(k => k.Kind)),
                            ["act_forms"] = Values(facets.ActForms),
                            ["binding_statuses"] = Values(facets.BindingStatuses),
                            ["languages"] = Values(facets.Languages),
                        };
                    }).ToArray()),
                ["hierarchies"] = Values(facetSets.SelectMany(f => f.Hierarchies)),
                ["domains"] = Values(facetSets.SelectMany(f => f.Domains)),
                ["source_classes"] = Values(cov.SelectMany(c => c.Kinds.Select(k => k.Kind))),
                ["act_forms"] = Values(facetSets.SelectMany(f => f.ActForms)),
                ["binding_statuses"] = Values(facetSets.SelectMany(f => f.BindingStatuses)),
                ["languages"] = Values(facetSets.SelectMany(f => f.Languages)),
            };

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
                ("eu-eurlex", "32016R0679", "GDPR"),
                ("eu-eurlex", "32013R0575", "Capital Requirements Regulation"),
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
                <p class="lede">Research Luxembourg and EU law as it stood on a chosen date. Read and compare
                exact official wording, with clear date semantics, sources and explicit gaps. Coverage always
                comes from the verified indexes mounted now.</p>
                """
                + $"""
                <!-- Read synchronously by the workspace on mount, so the doors never flash in or need a
                     round trip of their own. -->
                <script type="application/json" id="doors">{liveDoors.ToJsonString()}</script>
                <script type="application/json" id="search-facets">{searchFacets.ToJsonString()}</script>
                <script>document.documentElement.classList.add('workspace-loading')</script>
                """
                + $"""
                <!-- The workspace mounts here. Without JavaScript the page still explains itself
                     and every permalink below still works, those are server-rendered documents.
                     The three doors are repeated here as real links, because the block above is
                     JSON for the workspace to read and a crawler that runs no JavaScript followed
                     none of it: the front page carried twenty-four links and not one reached a
                     law. They live inside the noscript on purpose. A reader with JavaScript gets
                     the workspace's own richer version a moment later, and rendering both put the
                     same three names on the page twice. -->
                <div id="workspace"><noscript><p class="sub">The interactive workspace needs JavaScript.
                  Everything is also reachable as plain pages: <a href="/find">find a law</a>,
                  <a href="/changed">what changed</a>, <a href="/stories">stories</a>.</p>
                  {(liveDoors.Count == 0 ? "" : $"""
                  <p class="sub">Start with {string.Join(", ", liveDoors.Select(d =>
                      $"<a href=\"/{d!["work"]!.GetValue<string>().Replace(":", "/")}\">{H(d["label"]!.GetValue<string>())}</a>"))},
                  or <a href="/browse">browse all {cov.Sum(c => c.Groups):n0}</a>.</p>
                  """)}</noscript></div>
                """
                + $"""
                <div class="frontdoor">
                <p class="sub">
                <span class="badge">{cov.Sum(c => c.Groups):n0} legal works</span>
                <span class="badge">{cov.Sum(c => c.Rows):n0} dated versions</span>
                <span class="badge">{cov.Sum(c => c.TextServed):n0} with full text</span>
                <span class="badge">{H(cov.Select(c => c.EarliestValidFrom).Min())} → {H(cov.Select(c => c.LatestValidFrom).Max())}</span>
                <span class="badge ok" title="SHA-256 hashes and signed release manifests">source integrity verified</span></p>
                <p class="sub">Free assistant, daily limit. <a href="/ai">Connect your own AI</a>
                through the separately bounded public MCP endpoint.</p>

                <!-- A fork, not a menu. Three readers arrive here and they want different things; the
                     third one is the differentiator and was a footer link. A project that invites you to
                     audit it is making a claim the other two doors cannot make for it. -->
                <nav class="fork" aria-label="Where to go next">
                  <a href="/browse"><b>I want to read a law</b><span>The catalogue: every work, every dated version.</span></a>
                  <a href="/developers"><b>I want to build on this</b><span>MCP endpoint, {tools.Count} tools, the datasets, the licence.</span></a>
                  <a href="/about"><b>I want to know who built this</b><span>One engineer in Luxembourg, and two other systems built the same way.</span></a>
                </nav>
                </div>
                """
                + $$"""
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
                  .workspace-loading #workspace { min-height:430px }
                  @media (max-width:640px) { .workspace-loading #workspace { min-height:650px } }
                </style>
                <link rel="stylesheet" href="/app/workspace.css?v={{assetVersion}}">
                <script type="module" src="/app/workspace.js?v={{assetVersion}}"></script>
                """;
            // WebSite, so a crawler has an unambiguous name and publisher for the whole site.
            // Built as a JsonObject rather than written out as a string: JSON is mostly quotes and
            // braces, which is exactly what a C# raw literal reserves, and hand-quoting it is how
            // you ship a page carrying malformed structured data that nothing warns you about.
            var siteLd = new JsonObject
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "WebSite",
                ["name"] = "Lex",
                ["url"] = ctx.PublicBase,
                ["description"] = "Point-in-time retrieval of Luxembourg and reviewed-scope EU law, "
                                  + "with per-article history and verifiable provenance.",
                ["inLanguage"] = "en",
                ["publisher"] = new JsonObject
                {
                    ["@type"] = "Person", ["name"] = "Soufien Hajji", ["url"] = "https://soufien.lu",
                },
            }.ToJsonString();
            return Results.Content(Page("Luxembourg and EU law by publisher date", body, null, "ask",
                h1: "A law is not one document.", canonicalPath: "/", jsonLd: siteLd), "text/html");
        });

        return app;
    }
}
