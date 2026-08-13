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
        string Page(string title, string body, string? subtitle = null, string nav = "",
                    string? h1 = null, string? canonicalPath = null, string? jsonLd = null,
                    string? description = null, string? lang = null, bool assistant = true)
            => PageShell.Page(ctx.PublicBase, title, body, subtitle, nav, h1, canonicalPath,
                              jsonLd, description, lang, ctx.Options.CodeCommit, assistant);
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
            var workspaceUrl = "/?space=law&amp;work="
                + Uri.EscapeDataString($"{publisher}:{work}")
                + $"&amp;date={da:yyyy-MM-dd}&amp;to={db2:yyyy-MM-dd}&amp;mode=compare";
            sb.Append($"""
                <div class="card"><table class="kv">
                <tr><td>on {da:yyyy-MM-dd}</td><td class="mono"><a href="/{H(publisher)}/{H(work)}/{da:yyyy-MM-dd}">{H(a.Key)}</a> ({Interval(a)})
                &middot; <a href="{H(a.SourceUri)}">official source &nearr;</a></td></tr>
                <tr><td>on {db2:yyyy-MM-dd}</td><td class="mono"><a href="/{H(publisher)}/{H(work)}/{db2:yyyy-MM-dd}">{H(b.Key)}</a> ({Interval(b)})
                &middot; <a href="{H(b.SourceUri)}">official source &nearr;</a></td></tr>
                </table></div>
                <p><a href="{workspaceUrl}"><b>Open the structured article comparison &rarr;</b></a>
                <span class="sub">matched by provision anchor when continuity is sufficient; otherwise Lex refuses rather than inventing changes</span></p>
                """);

            if (a.Key == b.Key)
                sb.Append("<div class=\"notice\"><b>No change.</b> The same publisher version covers both selected dates.</div>");
            else if (a.TextPublic && b.TextPublic
                     && r.BuildBody(a) is { } bodyA && r.BuildBody(b) is { } bodyB)
                sb.Append(RenderDiff(bodyA, bodyB));
            else
                sb.Append($"""
                    <div class="notice"><b>Different versions applied</b>, but a text diff is unavailable here
                    (status <span class="mono">{(!a.TextAvailable || !b.TextAvailable ? "text_not_available" : "text_withheld")}</span>). Compare at the official source:
                    <a href="{H(a.SourceUri)}">version of {H(a.ValidFrom)}</a> vs
                    <a href="{H(b.SourceUri)}">version of {H(b.ValidFrom)}</a>.</div>
                    """);
            sb.Append(EnvelopeCard(r, IsProvisional(r, db2)));
            return Results.Content(Page($"What changed, {TitleShorten(DocTitle(b))}", sb.ToString(),
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
            var publisherVersionDates = UsesPublisherVersionDates(r);
            var sb = new StringBuilder();
            sb.Append($"<p><span class=\"badge\">{H(rows[^1].Kind)}</span> <span class=\"badge\">{rows.Select(v => v.Key).Distinct().Count()} version(s)</span> <a class=\"badge\" href=\"{H(rows[^1].SourceUri)}\">official text ↗</a></p>");
            sb.Append(VersionRail(publisher, work, rows, null));
            var todayVersion = r.AsOf(work, ctx.Today, FilterSet.All);
            var readDate = todayVersion is null ? rows[^1].ValidFrom : ctx.Today.ToString("yyyy-MM-dd");
            var readLabel = publisherVersionDates
                ? "Read the latest held publisher version"
                : todayVersion is null ? "Read the latest available publisher state" : "Read the text applicable today";
            sb.Append($"<p><a href=\"/{H(publisher)}/{H(work)}/{H(readDate)}\"><b>{readLabel} →</b></a></p>");
            sb.Append($"<details class=\"card\"><summary>Every version as a table</summary><table><tr><th>{(publisherVersionDates ? "publisher state" : "valid")}</th><th>as-of view</th><th>status</th><th>provenance</th></tr>");
            foreach (var v in rows)
                sb.Append($"""
                    <tr><td class="mono">{IntervalLabel(r, v)}</td>
                    <td><a href="/{H(publisher)}/{H(work)}/{H(v.ValidFrom)}">as of {H(v.ValidFrom)}</a></td>
                    <td>{(v.ValidTo is null
                        ? $"<span class=\"badge ok\">{(publisherVersionDates ? "latest held" : "open")}</span>"
                        : $"<span class=\"badge\">{(publisherVersionDates ? "earlier state" : "superseded")}</span>")}</td>
                    <td><a class="mono" href="/provenance/{H(v.Key)}">{H(v.Key.Split(':')[^1])}</a></td></tr>
                    """);
            sb.Append("</table></details>");
            sb.Append("<p class=\"sub\">Every state this document has been in, as asserted by the publisher. The corpus repo's <span class=\"mono\">git log</span> for this work shows the same history.</p>");
            sb.Append(EnvelopeCard(r, false));
            // The name of the law, then where it is from. Someone looking for this page types the
            // name of a law and a country, never the consolidation label the publisher prefixes.
            var jurisdictionInfo = JurisdictionOf(r);
            var jurisdiction = jurisdictionInfo.LawLabel;
            // Publisher titles end in a full stop. A title is a label, so the stop is dropped
            // rather than left to land mid-sentence as "Luxemburg.. Full text as it stood ...".
            var name = (StripConsolidationLabel(t) ?? t).TrimEnd('.', ' ');

            // The language of the WORK, not of whichever expression sorted last. The Constitution
            // has 37 French versions, one German and one Luxembourgish, and the last row happened
            // to be the Luxembourgish one, so the page declared lang="lb" over French text. Ties
            // break on the code so the answer does not depend on row order.
            var lang = rows.GroupBy(v => v.Language)
                           .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                           .First().Key;

            // Legislation is the schema.org type built for exactly this: a legal instrument with a
            // jurisdiction, a date, an identifier and a legal-force status. Using the type that
            // already exists beats inventing properties, and legislationLegalForce is the one
            // field here a general-purpose crawler cannot infer from the page for itself.
            var lawLd = new JsonObject
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "Legislation",
                ["name"] = t,
                ["url"] = $"{publicBase}/{publisher}/{work}",
                ["legislationIdentifier"] = rows[^1].GroupIdentifier,
                ["legislationJurisdiction"] = new JsonObject
                {
                    ["@type"] = jurisdictionInfo.SchemaType,
                    ["name"] = jurisdictionInfo.Name,
                },
                ["legislationDate"] = rows[0].ValidFrom,
                ["inLanguage"] = lang,
                ["temporalCoverage"] = rows[^1].ValidTo is null
                    ? $"{rows[0].ValidFrom}/.." : $"{rows[0].ValidFrom}/{rows[^1].ValidTo}",
                ["license"] = "https://creativecommons.org/licenses/by/4.0/",
                ["isBasedOn"] = rows[^1].SourceUri,
            };
            // An open consolidation interval proves only that this is the latest wording EUR-Lex
            // holds. It does not prove legal force. EU scope metadata carries that classification;
            // Luxembourg applicability intervals can support it directly.
            var force = rows[^1].BindingStatus switch
            {
                "in_force" => "https://schema.org/InForce",
                "not_in_force" => "https://schema.org/NotInForce",
                _ when !publisherVersionDates => rows[^1].ValidTo is null && !rows[^1].Withdrawn
                    ? "https://schema.org/InForce" : "https://schema.org/NotInForce",
                _ => null,
            };
            if (force is not null) lawLd["legislationLegalForce"] = force;
            if (rows[^1].Kind is { } kind) lawLd["legislationType"] = kind;

            // "version(s)" is fine in a table header a reader is already looking at. In a search
            // result it is the one string on the page written by a machine for a machine.
            // Count distinct version keys, not timeline rows: a multilingual version is one
            // version with several language rows.
            var versionCount = rows.Select(v => v.Key).Distinct().Count();
            var n = versionCount == 1 ? "1 version" : $"{versionCount} versions";
            var span = publisherVersionDates
                ? $"{n} dated {rows[0].ValidFrom} to {rows[^1].ValidFrom}"
                : rows[^1].ValidTo is null ? $"{n} from {rows[0].ValidFrom} to today"
                : $"{n}, {rows[0].ValidFrom} to {rows[^1].ValidTo}";
            var lawDesc = $"{name}. {(publisherVersionDates ? "Full text by official publisher-version date" : "Full text as it stood on any date")}: {span}, "
                        + "with per-article history and a link to the official text.";

            // Breadcrumbs alongside the Legislation record. A top-level array is valid JSON-LD and
            // is how you say two things about one page; it also gives a result a readable trail
            // instead of a bare URL, which is what a permalink of ours otherwise looks like.
            var graph = new JsonArray(lawLd, new JsonObject
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "BreadcrumbList",
                ["itemListElement"] = new JsonArray(
                    Crumb(1, "Catalogue", $"{publicBase}/browse"),
                    Crumb(2, jurisdiction, $"{publicBase}/browse?publisher={publisher}"),
                    Crumb(3, name, $"{publicBase}/{publisher}/{work}")),
            });
            return Results.Content(Page($"{name}, {jurisdiction}", sb.ToString(),
                "every version, on a time axis", "find", h1: name,
                canonicalPath: $"/{publisher}/{work}", jsonLd: graph.ToJsonString(),
                // html lang declares the language of THIS PAGE, not of the subject it
                // describes. A work page is a timeline: 553 words of English chrome, a French
                // title and a table of dates. Declaring it French because the law is French
                // told every crawler and every screen reader something untrue about the words
                // actually on the page. The version page is the opposite case and keeps its
                // expression language, because there the content really is 38,000 words of
                // French law and the chrome rounds to nothing.
                description: lawDesc), "text/html");
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
                return Results.Content(Page(work, sb0.ToString(), $"as of {d:yyyy-MM-dd}, honest refusal"), "text/html", statusCode: 404);
            }

            var all = r.Timeline(work);
            var publisherVersionDates = UsesPublisherVersionDates(r);
            var idx = all.FindIndex(x => x.Key == doc.Key && x.Language == doc.Language);
            var prev = idx > 0 ? all[idx - 1] : null;
            var next = idx >= 0 && idx < all.Count - 1 ? all[idx + 1] : null;

            var sb = new StringBuilder();
            sb.Append(VersionRail(publisher, work, all, doc.ValidFrom));
            // Unambiguous temporal-status banner (the legislation.gov.uk precedent): the reader
            // must never wonder WHICH state of the law they are looking at.
            sb.Append(publisherVersionDates
                ? $"""
                   <div class="notice" style="border-left-color:var(--ok)"><b>Official publisher wording state selected for {d:yyyy-MM-dd}.</b>
                   This is the consolidated version dated {H(doc.ValidFrom)}. Its interval on Lex's
                   publisher-version axis is {H(IntervalLabel(r, doc))}; that is not a claim about
                   entry into force or application.</div>
                   """
                : next is not null
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
            var primer = publisherVersionDates ? """
                <details class="card"><summary><b>New here? What am I looking at?</b></summary>
                <p>This is an <b>official consolidated text</b>: the original act with later
                amendments merged by EUR-Lex for the date shown above.</p>
                <p><b>The consolidation date is not an entry-into-force or application date.</b>
                It identifies a publisher wording state. The authentic legal acts remain those
                published in the Official Journal; Lex preserves the consolidated wording, source
                and hashes as a reading and comparison aid.</p>
                <p class="sub">Each article carries its own hash so you can verify that Lex served
                the indexed text unchanged, <a href="/verify">here is how</a>.</p></details>
                """ : """
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
                <tr><td>as of</td><td><b>{d:yyyy-MM-dd}</b> → {(publisherVersionDates ? "this publisher state was selected" : "this version applied")}</td></tr>
                <tr><td>{(publisherVersionDates ? "publisher state" : "valid")}</td><td class="mono">{IntervalLabel(r, doc)} <span class="badge">{H(doc.ValidTimeSource)}-asserted</span></td></tr>
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
                    sb.Append($"<a href=\"#{H(p.Anchor)}\" class=\"badge\">{RenderLegalInline(p.Num ?? p.Heading ?? p.Anchor)}</a> ");
                sb.Append("</p></details>");

                string? lastPath = null;
                var shown = 0;
                foreach (var p in provisions)
                {
                    if (p.Path is not null && p.Path != lastPath)
                    {
                        sb.Append($"<h2 style=\"margin-top:26px\">{RenderLegalInline(PlainLegalLabel(p.Path))}</h2>");
                        lastPath = p.Path;
                    }
                    var title = p.Num is null && p.Heading is null ? p.Anchor
                        : string.Join(", ", new[] { p.Num, p.Heading }.Where(s => !string.IsNullOrEmpty(s)));
                    sb.Append($"""
                        <div class="card" id="{H(p.Anchor)}">
                        <b>{RenderLegalInline(title)}</b>
                        <a class="sub mono" href="#{H(p.Anchor)}" title="permalink to this provision">#{H(p.Anchor)}</a>
                        {(p.ArticleValidFrom is not null && p.ArticleValidFrom != doc.ValidFrom ? $"<span class=\"badge\">applicable {H(p.ArticleValidFrom)}</span>" : "")}
                        <div class="lawbody legal-markdown">{RenderLegalMarkdown(p.TextMd)}</div>
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
                sb.Append(MissingTextBox(doc));
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

            // Any date inside a version's interval renders that same version, so /…/2020-03-04 and
            // /…/2020-08-11 can be byte-identical pages on different URLs. Left alone that is a few
            // thousand self-inflicted duplicates competing with each other; the canonical names the
            // date the version actually starts, which is the one URL worth ranking.
            var vName = (StripConsolidationLabel(DocTitle(doc)) ?? DocTitle(doc)).TrimEnd('.', ' ');
            var vDesc = publisherVersionDates
                ? $"{vName}, official consolidated publisher version dated {doc.ValidFrom}. This date identifies a wording state, not entry into force."
                : $"{vName}, as it stood on {d:yyyy-MM-dd}. This version applied from {doc.ValidFrom}"
                  + (doc.ValidTo is null ? " and is still in force." : $" to {doc.ValidTo}.");
            return Results.Content(Page($"{vName}, as of {d:yyyy-MM-dd}", sb.ToString(),
                $"as it stood on <span class=\"asof\">{d:yyyy-MM-dd}</span>, " +
                $"permalink: /{H(publisher)}/{H(work)}/{d:yyyy-MM-dd}",
                h1: vName, canonicalPath: $"/{publisher}/{work}/{doc.ValidFrom}",
                description: vDesc, lang: doc.Language), "text/html");
        });

        return app;
    }

    /// <summary>One step of a breadcrumb trail, in the shape schema.org wants.</summary>
    private static JsonObject Crumb(int position, string name, string url) => new()
    {
        ["@type"] = "ListItem", ["position"] = position, ["name"] = name, ["item"] = url,
    };

    /// <summary>
    /// Human and schema.org labels come from the mounted artifact, not from the collection ID.
    /// The explicit EU/LU names are presentation rules for today's indexes; an added jurisdiction
    /// still receives an honest label instead of accidentally being called Luxembourg law.
    /// </summary>
    private static (string LawLabel, string Name, string SchemaType) JurisdictionOf(LexIndexReader reader)
    {
        var code = reader.Stamp.GetValueOrDefault("jurisdiction", "").Trim();
        return code.ToUpperInvariant() switch
        {
            "EU" => ("EU law", "European Union", "AdministrativeArea"),
            "LU" => ("Luxembourg law", "Luxembourg", "Country"),
            _ => ($"{(code.Length > 0 ? code : reader.Stamp.GetValueOrDefault("publisher_name", reader.Collection))} law",
                  code.Length > 0 ? code : reader.Stamp.GetValueOrDefault("publisher_name", reader.Collection),
                  "AdministrativeArea"),
        };
    }
}
