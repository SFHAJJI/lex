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
            (DocRow? Document, DateOnly Date, IReadOnlyList<DocRow> Choices, bool Invalid) ResolveBoundary(
                string coordinate)
            {
                if (TryIsoDate(coordinate, out var date))
                {
                    var choices = r.VersionsEffectiveOn(work, date);
                    return choices.Count > 1
                        ? (null, date, choices.Take(20).ToArray(), false)
                        : (choices.Count == 1 ? choices[0] : r.AsOf(work, date, FilterSet.All),
                            date, [], false);
                }
                if (coordinate.Length < 10 || !TryIsoDate(coordinate[..10], out var exactDate))
                    return (null, default, [], true);
                var exact = r.VersionByCoordinate(work, exactDate, coordinate);
                return exact is null
                    ? (null, default, [], true)
                    : (exact, exactDate, [], false);
            }

            var fromBoundary = ResolveBoundary(dateA);
            var toBoundary = ResolveBoundary(dateB);
            if (fromBoundary.Invalid || toBoundary.Invalid)
                return Results.Content(Page("Bad version coordinate",
                    "<p>Each comparison boundary must be YYYY-MM-DD or a held opaque version key.</p>"),
                    "text/html", statusCode: 400);
            if (fromBoundary.Choices.Count > 0 || toBoundary.Choices.Count > 0)
            {
                string Choices(string label, IReadOnlyList<DocRow> values, string other, bool first) =>
                    values.Count == 0 ? "" : $"<h2>{H(label)}</h2><ul>" + string.Join("", values.Select(version =>
                        $"<li><a class=\"mono\" href=\"/{H(publisher)}/{H(work)}/diff/"
                        + (first ? $"{H(VersionCoordinate(version))}/{H(other)}" : $"{H(other)}/{H(VersionCoordinate(version))}")
                        + $"\">{H(version.Key)}</a></li>")) + "</ul>";
                return Results.Content(Page("Ambiguous publisher version",
                    "<div class=\"notice\">status <span class=\"mono\">ambiguous_version</span>, "
                    + "the publisher exposes separately identified states on a selected date. "
                    + "Choose each exact comparison boundary.</div>"
                    + Choices("From version", fromBoundary.Choices, dateB, true)
                    + Choices("To version", toBoundary.Choices, dateA, false)),
                    "text/html", statusCode: 409);
            }

            var da = fromBoundary.Date;
            var db2 = toBoundary.Date;
            var a = fromBoundary.Document;
            var b = toBoundary.Document;
            if (a is null || b is null)
                return Results.Content(Page("No version for date",
                    $"<p>status <span class=\"mono\">no_version_for_date</span>, resolved: {da:yyyy-MM-dd}={(a is not null)}, {db2:yyyy-MM-dd}={(b is not null)}. See the <a href=\"/{H(publisher)}/{H(work)}\">timeline</a>.</p>"),
                    "text/html", statusCode: 404);

            var sb = new StringBuilder();
            var workspaceUrl = "/?space=law&amp;work="
                + Uri.EscapeDataString($"{publisher}:{work}")
                + $"&amp;date={da:yyyy-MM-dd}&amp;to={db2:yyyy-MM-dd}&amp;mode=compare"
                + $"&amp;from_version_key={Uri.EscapeDataString(VersionCoordinate(a))}"
                + $"&amp;to_version_key={Uri.EscapeDataString(VersionCoordinate(b))}";
            sb.Append($"""
                <div class="card"><table class="kv">
                <tr><td>on {da:yyyy-MM-dd}</td><td class="mono"><a href="/{H(publisher)}/{H(work)}/{H(VersionCoordinate(a))}">{H(a.Key)}</a> ({Interval(a)})
                &middot; {OfficialLink(a.SourceUri, "official source &nearr;")}</td></tr>
                <tr><td>on {db2:yyyy-MM-dd}</td><td class="mono"><a href="/{H(publisher)}/{H(work)}/{H(VersionCoordinate(b))}">{H(b.Key)}</a> ({Interval(b)})
                &middot; {OfficialLink(b.SourceUri, "official source &nearr;")}</td></tr>
                </table></div>
                <p><a href="{workspaceUrl}"><b>Open the structured article comparison &rarr;</b></a>
                <span class="sub">matched by provision anchor when continuity is sufficient; otherwise Lex refuses rather than inventing changes</span></p>
                """);

            var typedGap = r.ProvisionGapCount(LexIndexReader.RidOf(a)) > 0
                           || r.ProvisionGapCount(LexIndexReader.RidOf(b)) > 0;
            if (typedGap)
                sb.Append($"""
                    <div class="notice"><b>A text diff is unavailable here</b>
                    (status <span class="mono">{ComparisonTextStatus(r, a, b)}</span>).
                    One or both selected versions contain a typed text gap, so Lex will not compare
                    a partial body as if it were complete. Compare at the official source:
                    {OfficialLink(a.SourceUri, $"version of {H(a.ValidFrom)}")} vs
                    {OfficialLink(b.SourceUri, $"version of {H(b.ValidFrom)}")}.</div>
                    """);
            else if (a.Key == b.Key)
                sb.Append("<div class=\"notice\"><b>No change.</b> The same publisher version covers both selected dates.</div>");
            else if (a.TextPublic && b.TextPublic
                     && r.BuildBody(a) is { } bodyA && r.BuildBody(b) is { } bodyB)
                sb.Append(RenderDiff(bodyA, bodyB));
            else
            {
                sb.Append($"""
                    <div class="notice"><b>Different versions applied</b>, but a text diff is unavailable here
                    (status <span class="mono">{ComparisonTextStatus(r, a, b)}</span>).
                    Compare at the official source:
                    {OfficialLink(a.SourceUri, $"version of {H(a.ValidFrom)}")} vs
                    {OfficialLink(b.SourceUri, $"version of {H(b.ValidFrom)}")}.</div>
                    """);
            }
            sb.Append(EnvelopeCard(r, IsProvisional(r, db2)));
            return Results.Content(Page($"What changed, {TitleShorten(DocTitle(b))}", sb.ToString(),
                $"{da:yyyy-MM-dd} → {db2:yyyy-MM-dd} · no interpretation, just the text delta",
                canonicalPath: $"/{publisher}/{work}/diff/{VersionCoordinate(a)}/{VersionCoordinate(b)}"), "text/html");
        });

        app.MapGet($"/{pubRoute}/{{work}}", (string publisher, string work) =>
        {
            var r = Reader(publisher);
            if (r is null) return Results.Content(Page("Unknown publisher", $"<p>No index mounted for <b>{H(publisher)}</b>. See <a href=\"/coverage\">coverage</a>.</p>"), "text/html", statusCode: 404);
            var rows = r.TimelineVersions(work).Select(version => version.Version).ToList();
            if (rows.Count == 0)
                // The refusal carries the nearest held records. Phase 0 frozen copy, Decision 41.
                return Results.Content(
                    Page("Instrument not found in held records",
                         TrustNotices.UnknownWork(r, publisher, work)),
                    "text/html", statusCode: 404);

            var t = DocTitle(rows[^1]);
            var publisherVersionDates = UsesPublisherVersionDates(r);
            var sb = new StringBuilder();
            sb.Append($"<p><span class=\"badge\">{H(rows[^1].Kind)}</span> <span class=\"badge\">{rows.Select(v => v.Key).Distinct().Count()} version(s)</span> {OfficialLink(rows[^1].SourceUri, "official text ↗", "badge")}</p>");
            sb.Append(VersionRail(publisher, work, rows, null));
            var todayVersion = r.AsOf(work, ctx.Today, FilterSet.All);
            var latest = todayVersion ?? rows[^1];
            var latestChoices = r.VersionsEffectiveOn(work, ParseIsoDate(latest.ValidFrom));
            var readCoordinate = latestChoices.Count > 1 ? null : VersionCoordinate(latest);
            var readLabel = publisherVersionDates
                ? "Read the latest held publisher version"
                : todayVersion is null ? "Read the latest available publisher state" : "Read the text applicable today";
            sb.Append(readCoordinate is null
                ? "<p><b>Choose one exact publisher state from the timeline below.</b></p>"
                : $"<p><a href=\"/{H(publisher)}/{H(work)}/{H(readCoordinate)}\"><b>{readLabel} →</b></a></p>");
            sb.Append($"<details class=\"card\"><summary>Every version as a table</summary><table><tr><th>{(publisherVersionDates ? "publisher state" : "valid")}</th><th>as-of view</th><th>status</th><th>provenance</th></tr>");
            foreach (var v in rows)
                sb.Append($"""
                    <tr><td class="mono">{IntervalLabel(r, v)}</td>
                    <td><a href="/{H(publisher)}/{H(work)}/{H(VersionCoordinate(v))}">as of {H(v.ValidFrom)}</a></td>
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
                // No license here. A Legislation node describes the PUBLISHER'S legal text, and
                // this line asserted CC BY 4.0 for every work of every publisher, hardcoded, on
                // the authority of nothing. Whether a publisher's text may be redistributed under
                // a named licence is exactly what the licence evidence work exists to establish,
                // and its own outcome set has three ways for the answer to be no. Machine-readable
                // and on every page made it the largest unsupported claim on the site.
                //
                // It stays out until an evidence-backed admission can populate it per work rather
                // than a constant asserting it for all of them.
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

        app.MapGet($"/{pubRoute}/{{work}}/{{coordinate}}", (string publisher, string work, string coordinate) =>
        {
            var date = coordinate;
            var r = Reader(publisher);
            if (r is null) return Results.Content(Page("Unknown publisher", $"<p>No index mounted for <b>{H(publisher)}</b>.</p>"), "text/html", statusCode: 404);
            DocRow? doc;
            DateOnly d = default;
            var exact = date.Length > 10 && TryIsoDate(date[..10], out d)
                ? r.VersionByCoordinate(work, d, date)
                : null;
            if (exact is not null)
            {
                doc = exact;
            }
            else
            {
                if (!TryIsoDate(date, out d))
                    return Results.Content(Page("Bad version coordinate",
                        $"<p>'{H(date)}' is neither YYYY-MM-DD nor a held version key.</p>"),
                        "text/html", statusCode: 400);
                var sameDate = r.VersionsEffectiveOn(work, d);
                if (sameDate.Count > 1)
                {
                    var choices = string.Join("", sameDate.Take(20).Select(version =>
                        $"<li><a class=\"mono\" href=\"/{H(publisher)}/{H(work)}/{H(VersionCoordinate(version))}\">"
                        + $"{H(version.Key)}</a></li>"));
                    var choiceCount = sameDate.Count > 20 ? "more than 20" : sameDate.Count.ToString();
                    return Results.Content(Page("Ambiguous publisher version",
                        $"<div class=\"notice\">status <span class=\"mono\">ambiguous_version</span>, "
                        + $"the publisher exposes {choiceCount} separately identified states dated {d:yyyy-MM-dd}. "
                        + "Choose the exact publisher version:</div><ul>" + choices + "</ul>"),
                        "text/html", statusCode: 409);
                }
                doc = r.AsOf(work, d, FilterSet.All);
            }
            if (doc is null)
            {
                if (!r.WorkExists(work))
                    return Results.Content(
                        Page("Instrument not found in held records",
                             TrustNotices.UnknownWork(r, publisher, work)),
                        "text/html", statusCode: 404);
                var timeline = r.TimelineVersions(work).Select(version => version.Version).ToList();
                var sb0 = new StringBuilder();
                sb0.Append($"""
                    <div class="notice">status <span class="mono">no_version_for_date</span>, the work exists, but no
                    version covers <b>{d:yyyy-MM-dd}</b>. The publisher's digitised history for this work covers:</div>
                    """);
                sb0.Append("<ul>");
                foreach (var v in timeline.Take(30))
                    sb0.Append($"<li><a href=\"/{H(publisher)}/{H(work)}/{H(VersionCoordinate(v))}\" class=\"mono\">{Interval(v)}</a></li>");
                sb0.Append("</ul>");
                sb0.Append(EnvelopeCard(r, IsProvisional(r, d)));
                return Results.Content(Page(work, sb0.ToString(), $"as of {d:yyyy-MM-dd}, honest refusal"), "text/html", statusCode: 404);
            }

            var all = r.TimelineVersions(work).Select(version => version.Version).ToList();
            var publisherVersionDates = UsesPublisherVersionDates(r);
            var idx = all.FindIndex(x => x.Key == doc.Key);
            var prev = idx > 0 ? all[idx - 1] : null;
            var next = idx >= 0 && idx < all.Count - 1 ? all[idx + 1] : null;

            var sb = new StringBuilder();
            sb.Append(VersionRail(publisher, work, all, VersionCoordinate(doc)));
            // Unambiguous temporal-status banner (the legislation.gov.uk precedent): the reader
            // must never wonder WHICH state of the law they are looking at.
            sb.Append(publisherVersionDates
                ? $"""
                   <div class="notice" style="border-left-color:var(--ok)"><b>Official publisher wording state selected for {d:yyyy-MM-dd}.</b>
                   This is the consolidated version dated {H(doc.ValidFrom)}. Its interval on Lex's
                   publisher-version axis is {IntervalLabel(r, doc)}; that is not a claim about
                   entry into force or application.</div>
                   """
                : next is not null
                ? $"""
                   <div class="notice"><b>Point-in-time view as at {d:yyyy-MM-dd}.</b> This version has been
                   <b>superseded</b>, it applied {Interval(doc)}. <a href="/{H(publisher)}/{H(work)}">Jump to the
                   version in force today</a> or <a href="/{H(publisher)}/{H(work)}/diff/{H(VersionCoordinate(doc))}/{H(VersionCoordinate(next))}">see
                   exactly what changed next</a>.</div>
                   """
                : $"""
                   <div class="notice" style="border-left-color:var(--ok)"><b>Point-in-time view as at {d:yyyy-MM-dd}.</b>
                   This is the latest state the publisher has consolidated, valid {Interval(doc)}.</div>
                   """);
            // Phase 0 trust notice (Decision 41): a consolidated state dated before the
            // publisher's application date must say so. It renders only when an indexed
            // application-date fact exists; the evidence source answers null until EU typed
            // dates land, so this line is inert today by design, not by accident.
            sb.Append(TrustNotices.PreApplicationState(
                doc, TrustNotices.FindPreApplicationFact(r, doc)) ?? "");
            var rid = LexIndexReader.RidOf(doc);
            var provisions = doc.TextPublic ? r.Provisions(rid) : [];
            var gaps = r.ProvisionGaps(rid);

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
                <p class="sub">Each displayed provision carries its own hash so you can verify that Lex served
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
                Each displayed provision carries its own hash so you can prove it was not tampered with,
                <a href="/verify">here is how</a>.</p></details>
                """;
            if (gaps.Count > 0)
                primer = primer.Replace(
                    "Each displayed provision carries its own hash",
                    "Each displayed publisher-text provision carries its own hash; a typed gap carries no text hash because no wording was certified",
                    StringComparison.Ordinal);
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

            if (gaps.Count == 0)
            {
                if (provisions.Count > 0)
                {
                    sb.Append($"""
                        <div class="notice" style="border-left-color:var(--ok)"><b>Text included, per-article reading view.</b>
                        Deterministic extraction of the verbatim retrieved document; each displayed provision carries its own hash and anchor.
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
                        var derogation = TrustNotices.TemporaryDerogation(
                            r, publisher, doc.GroupKey, p.Anchor);
                        sb.Append($"""
                            <div class="card" id="{H(p.Anchor)}">
                            <b>{RenderLegalInline(title)}</b>
                            <a class="sub mono" href="#{H(p.Anchor)}" title="permalink to this provision">#{H(p.Anchor)}</a>
                            {(p.ArticleValidFrom is not null && p.ArticleValidFrom != doc.ValidFrom ? $"<span class=\"badge\">applicable {H(p.ArticleValidFrom)}</span>" : "")}{derogation ?? ""}
                            <div class="lawbody legal-markdown">{RenderLegalMarkdown(p.TextMd)}</div>
                            </div>
                            """);
                        shown++;
                        if (shown >= 400) { sb.Append($"<p class=\"sub\">,  {provisions.Count - shown:n0} further provisions omitted from this view; retrieve them via the MCP tools , </p>"); break; }
                    }
                    sb.Append($"""
                        <details class="card"><summary><b>Provenance and validity</b>
                        <span class="sub">dates, identifier, hash</span></summary>{record}</details>
                        """);
                }
                else
                {
                    sb.Append(MissingTextBox(doc, PublisherTextGateOpen(r)));
                    sb.Append($"""<div class="card">{record}</div>""");
                }
            }
            else
            {
                var displayRows = provisions
                    .Select(provision => (
                        provision.Seq, provision.Anchor, provision.Num, provision.Heading,
                        provision.Path, provision.ArticleValidFrom,
                        Text: (ProvisionRow?)provision, Gap: (ProvisionGapRow?)null))
                    .Concat(gaps.Select(gap => (
                        gap.Seq, gap.Anchor, gap.Num, gap.Heading,
                        gap.Path, gap.ArticleValidFrom,
                        Text: (ProvisionRow?)null, Gap: (ProvisionGapRow?)gap)))
                    .OrderBy(row => row.Seq)
                    .ToList();
                sb.Append($"""
                    <div class="notice"><b>{(provisions.Count == 0 ? "Publisher text unavailable." : "Partial publisher text.")}</b>
                    Lex holds verified wording for {provisions.Count:n0} provision(s) and {gaps.Count:n0} typed coordinate(s) whose wording could not be certified.
                    Gap cards preserve publisher order, anchor, reason and official source without inventing text or a text hash.
                    {H(r.Stamp.GetValueOrDefault("attribution"))}</div>
                    <details class="card"><summary><b>Outline, {displayRows.Count} provisions</b></summary><p>
                    """);
                foreach (var row in displayRows)
                    sb.Append($"<a href=\"#{H(row.Anchor)}\" class=\"badge{(row.Gap is null ? "" : " warn")}\">{RenderLegalInline(row.Num ?? row.Heading ?? row.Anchor)}</a> ");
                sb.Append("</p></details>");

                string? lastPath = null;
                var shown = 0;
                foreach (var row in displayRows)
                {
                    if (row.Path is not null && row.Path != lastPath)
                    {
                        sb.Append($"<h2 style=\"margin-top:26px\">{RenderLegalInline(PlainLegalLabel(row.Path))}</h2>");
                        lastPath = row.Path;
                    }
                    var title = row.Num is null && row.Heading is null ? row.Anchor
                        : string.Join(", ", new[] { row.Num, row.Heading }.Where(s => !string.IsNullOrEmpty(s)));
                    // Phase 0 trust notice (Decisions 41 and 44): rendered inside the provision
                    // card it concerns, only when its typed evidence condition holds.
                    var derogation = TrustNotices.TemporaryDerogation(
                        r, publisher, doc.GroupKey, row.Anchor);
                    if (row.Text is { } text)
                        sb.Append($"""
                            <div class="card" id="{H(row.Anchor)}">
                            <b>{RenderLegalInline(title)}</b>
                            <a class="sub mono" href="#{H(row.Anchor)}" title="permalink to this provision">#{H(row.Anchor)}</a>
                            {(row.ArticleValidFrom is not null && row.ArticleValidFrom != doc.ValidFrom ? $"<span class=\"badge\">applicable {H(row.ArticleValidFrom)}</span>" : "")}{derogation ?? ""}
                            <div class="lawbody legal-markdown">{RenderLegalMarkdown(text.TextMd)}</div>
                            </div>
                            """);
                    else if (row.Gap is { } gap)
                    {
                        var officialLink = OfficialGapLink(
                            permalink: null,
                            eli: gap.Eli,
                            sourceUri: doc.SourceUri,
                            officialSource: null);
                        sb.Append($"""
                            <div class="card" id="{H(row.Anchor)}">
                            <b>{RenderLegalInline(title)}</b>
                            <a class="sub mono" href="#{H(row.Anchor)}" title="permalink to this provision">#{H(row.Anchor)}</a>
                            {(row.ArticleValidFrom is not null && row.ArticleValidFrom != doc.ValidFrom ? $"<span class=\"badge\">applicable {H(row.ArticleValidFrom)}</span>" : "")}{derogation ?? ""}
                            <div class="notice"><b>Text unavailable.</b> Lex preserved this publisher coordinate but could not certify wording for it
                            (status <span class="mono">{H(gap.TextUnavailableReason)}</span>).{officialLink}</div>
                            </div>
                            """);
                    }
                    shown++;
                    if (shown >= 400) { sb.Append($"<p class=\"sub\">,  {displayRows.Count - shown:n0} further provisions omitted from this view; retrieve them via the MCP tools , </p>"); break; }
                }
                // The receipt, after the goods. This table was the first thing on the page, above the law
                // it describes, which is backwards for the nineteen readers in twenty who came to read the
                // law. It is still one click away for the twentieth, who is the reason it exists.
                sb.Append($"""
                    <details class="card"><summary><b>Provenance and validity</b>
                    <span class="sub">dates, identifier, hash</span></summary>{record}</details>
                    """);
            }
            sb.Append(primer);
            sb.Append("<p>");
            if (prev is not null)
            {
                sb.Append($"<a href=\"/{H(publisher)}/{H(work)}/{H(VersionCoordinate(prev))}\">← previous version ({H(prev.ValidFrom)})</a> &nbsp;&nbsp;");
                if (doc.TextPublic) sb.Append($"<a href=\"/{H(publisher)}/{H(work)}/diff/{H(VersionCoordinate(prev))}/{H(VersionCoordinate(doc))}\">what changed?</a> &nbsp;&nbsp;");
            }
            sb.Append($"<a href=\"/{H(publisher)}/{H(work)}\">timeline</a>");
            if (next is not null) sb.Append($" &nbsp;&nbsp;<a href=\"/{H(publisher)}/{H(work)}/{H(VersionCoordinate(next))}\">next version ({H(next.ValidFrom)}) →</a>");
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
                $"permalink: /{H(publisher)}/{H(work)}/{H(VersionCoordinate(doc))}",
                h1: vName, canonicalPath: $"/{publisher}/{work}/{VersionCoordinate(doc)}",
                description: vDesc, lang: doc.Language), "text/html");
        });

        return app;
    }

    /// <summary>
    /// Selects one external publisher source without coalescing candidates before validation.
    /// Candidate priority matches the browser: permalink, ELI, signed source URI, legacy source.
    /// </summary>
    internal static string? OfficialGapSource(
        string? permalink,
        string? eli,
        string? sourceUri,
        string? officialSource)
    {
        foreach (var candidate in new[] { permalink, eli, sourceUri, officialSource })
            if (candidate is not null
                && Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return uri.AbsoluteUri;
        return null;
    }

    internal static string OfficialGapLink(
        string? permalink,
        string? eli,
        string? sourceUri,
        string? officialSource)
    {
        var source = OfficialGapSource(permalink, eli, sourceUri, officialSource);
        return source is null ? "" :
            $" <a href=\"{H(source)}\" rel=\"noopener\">Open the official publisher source</a>.";
    }

    private static bool TryIsoDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out date);

    private static DateOnly ParseIsoDate(string value) =>
        TryIsoDate(value, out var date) ? date
            : throw new InvalidDataException($"Held version has invalid ISO date '{value}'.");

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
