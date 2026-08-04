using System.Text;
using System.Text.Json.Nodes;
using Lex.Index;
using static Lex.Web.PageShell;
using static Lex.Web.Fragments;

namespace Lex.Web;

/// <summary>
/// Reading one law: its timeline, its text as it stood on a date, and the difference between two dates. These are the permalinks everything else on the site points at, and the only routes constrained to the publishers actually mounted.
/// </summary>
public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocuments(this IEndpointRouteBuilder app, WebContext ctx)
    {
        var readers = ctx.Registry.All;
        var publicBase = ctx.PublicBase;
        string Page(string title, string body, string? subtitle = null, string nav = "", string? h1 = null)
            => PageShell.Page(ctx.PublicBase, title, body, subtitle, nav, h1);
        LexIndexReader? Reader(string publisher) => ctx.Registry.All.GetValueOrDefault(publisher);

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

        return app;
    }
}
