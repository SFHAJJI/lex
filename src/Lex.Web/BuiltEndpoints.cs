using System.Text;
using Markdig;
using static Lex.Web.PageShell;

namespace Lex.Web;

/// <summary>
/// Renders the architecture dossier from one set of embedded, reviewable Markdown pages and
/// serves only the diagrams owned by that dossier. The C# layer owns routes and presentation;
/// the architectural claims remain in docs/architecture/pages.
/// </summary>
public static class BuiltEndpoints
{
    internal sealed record DossierPage(
        string Slug,
        string Path,
        string Label,
        string Subtitle,
        string Description);

    internal static readonly IReadOnlyList<DossierPage> Pages =
    [
        new("overview", "/built", "Overview", "The system in sixty seconds and the decisions it enables.",
            "The business purpose, system boundary and reading paths for the Lex architecture."),
        new("model", "/built/model", "Legal model", "Identity, legal time, observation time and honest gaps.",
            "The temporal legal model behind dated Luxembourg and EU law."),
        new("data", "/built/data", "Data authority", "Official sources, deterministic derivation and verifiable provenance.",
            "How Lex turns official publisher records into signed, reproducible data artifacts."),
        new("retrieval", "/built/retrieval", "Retrieval", "Identity before ranking, measured modes and explicit failure classes.",
            "The retrieval architecture, top-k policy and failure analysis behind grounded answers."),
        new("assistant", "/built/assistant", "Assistant", "A bounded agent around a deterministic legal core.",
            "The typed planning, evidence and optional composition architecture of the Lex assistant."),
        new("release", "/built/release", "Release", "Candidate evaluation, signed promotion, rollback and retention.",
            "The release state machine and separation of authority used to deploy Lex."),
        new("decisions", "/built/decisions", "Trade-offs", "The choices worth challenging and the costs they admit.",
            "A curated architecture decision record for Lex."),
        new("incidents", "/built/incidents", "Incidents", "Failures converted into permanent product guards.",
            "Architecture incidents, root causes, safeguards and lessons from Lex."),
        new("limits", "/built/limits", "Limits and scale", "Known constraints, scaling triggers and deliberately deferred work.",
            "The current limits, capacity triggers and next architecture moves for Lex."),
    ];

    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .DisableHtml()
        .Build();

    private static readonly HashSet<string> DiagramNames =
    [
        "system", "legal-model", "data-authority", "retrieval", "assistant-boundary", "assistant",
        "memory", "release", "incident", "scale",
    ];

    private static readonly IReadOnlyDictionary<string, string> MarkdownBySlug = Pages
        .ToDictionary(page => page.Slug, page => ReadResource(
            $"Lex.Web.architecture.pages.{page.Slug}.md"), StringComparer.Ordinal);

    private static string ReadResource(string name)
    {
        using var stream = typeof(BuiltEndpoints).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded architecture resource '{name}' is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string RenderMarkdown(string markdown, bool full)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (full)
        {
            for (var i = 0; i < lines.Length; i++)
                if (lines[i].StartsWith('#'))
                    lines[i] = "#" + lines[i];
        }
        else if (lines.Length > 0 && lines[0].StartsWith("# ", StringComparison.Ordinal))
        {
            lines[0] = "";
        }

        var html = Markdown.ToHtml(string.Join('\n', lines), MarkdownPipeline)
            .Replace("<table>",
                "<div class=\"dossier-table\" tabindex=\"0\" role=\"region\" aria-label=\"Scrollable architecture table\"><table>",
                StringComparison.Ordinal)
            .Replace("</table>", "</table></div>", StringComparison.Ordinal)
            .Replace("<pre>",
                "<pre tabindex=\"0\" aria-label=\"Scrollable architecture diagram or code\">",
                StringComparison.Ordinal);

        return WrapDiagram(
            WrapDiagram(html, "assistant-boundary.svg", "dossier-boundary",
                "Scrollable assistant ownership and trust-boundary diagram"),
            "assistant.svg", "dossier-sequence",
            "Scrollable assistant sequence diagram");
    }

    private static string WrapDiagram(string html, string diagram, string cssClass, string label)
    {
        var imageStart = $"<p><img src=\"/built/diagrams/{diagram}\"";
        var start = html.IndexOf(imageStart, StringComparison.Ordinal);
        if (start < 0) return html;
        var end = html.IndexOf("</p>", start, StringComparison.Ordinal);
        if (end < 0)
            throw new InvalidOperationException($"The assistant diagram '{diagram}' has no paragraph boundary.");

        var image = html[(start + 3)..end];
        var wrapper =
            $"<div class=\"{cssClass}\" tabindex=\"0\" role=\"region\" aria-label=\"{label}\">";
        return html[..start] + wrapper + image + "</div>" + html[(end + 4)..];
    }

    private static string Tabs(string activeSlug) =>
        "<nav class=\"dossier-tabs\" aria-label=\"Architecture dossier\">"
        + string.Join("", Pages.Select(page =>
            $"<a href=\"{page.Path}\"{(page.Slug == activeSlug ? " aria-current=\"page\"" : "")}>{H(page.Label)}</a>"))
        + "<a class=\"dossier-print\" href=\"/architecture/dossier\">Complete dossier</a>"
        + "</nav>";

    private static string Article(DossierPage page, string addendum) =>
        $"{Tabs(page.Slug)}<article class=\"architecture-dossier\">"
        + RenderMarkdown(MarkdownBySlug[page.Slug], full: false)
        + addendum
        + "</article>";

    private static string FullArticle(Func<string, string> addendum) =>
        "<article class=\"architecture-dossier architecture-dossier-full\">"
        + string.Join("", Pages.Select(page =>
            RenderMarkdown(MarkdownBySlug[page.Slug], full: true) + addendum(page.Slug)))
        + "</article>";

    public static IEndpointRouteBuilder MapBuilt(this IEndpointRouteBuilder app, WebContext ctx)
    {
        string Page(string title, string body, string subtitle, string canonicalPath, string description,
                    bool noIndex = false)
            => PageShell.Page(ctx.PublicBase, title, body, subtitle, "how", canonicalPath: canonicalPath,
                              description: description, assetVersion: ctx.Options.CodeCommit,
                              extraHead: (noIndex
                                  ? "<meta name=\"robots\" content=\"noindex,follow\">"
                                  : "")
                                  + $"<link rel=\"stylesheet\" href=\"/dossier.css?v={Uri.EscapeDataString(ctx.Options.CodeCommit ?? "dev")}\">");

        static string StatusBadge(string status) =>
            $"<span class=\"badge{(status == "shipped" ? " ok" : status == "gated" ? " warn" : "")}\">{H(status)}</span>";

        string MountedReleaseEvidence()
        {
            var mounted = ctx.Registry.All.Values
                .Select(reader => (Reader: reader, Coverage: reader.Coverage()))
                .OrderBy(item => item.Coverage.Collection, StringComparer.Ordinal).ToList();
            var hybridCollections = ctx.Registry.All.Values.Where(reader => reader.HybridReady)
                .Select(reader => reader.Collection).Order(StringComparer.Ordinal).ToList();
            var retrieval = hybridCollections.Count == 0
                ? "keyword only; no compatible hybrid artifact is mounted"
                : $"keyword default; compatible local hybrid artifacts mounted for {string.Join(", ", hybridCollections)}";
            var coverageRows = string.Join("", mounted.Select(mountedIndex =>
            {
                var item = mountedIndex.Coverage;
                return $"<tr><td>{H(item.Collection)}</td><td class=\"mono\">{item.Groups:n0}</td>"
                    + $"<td class=\"mono\">{item.Versions:n0}</td>"
                    + $"<td class=\"mono\">{H(item.Stamp.GetValueOrDefault("schema", "unknown"))}</td>"
                    + $"<td class=\"mono\">{H(item.Stamp.GetValueOrDefault("corpus_commit", "unknown"))}</td>"
                    + $"<td>{(mountedIndex.Reader.SignatureValid ? "<span class=\"badge ok\">valid</span>" : "<span class=\"badge warn\">unsigned</span>")}</td></tr>";
            }));

            return $"""
                <h2 id="mounted-release">Mounted release evidence</h2>
                <p>The release model above is the invariant. This table reads process configuration and
                verified mounted indexes at request time. It reports the revision's identities and
                capabilities; signed promotion receipts separately prove whether traffic was authorized.</p>
                <div class="dossier-table" tabindex="0" role="region" aria-label="Mounted release identities"><table class="kv">
                <tr><th>mounted retrieval</th><td>{H(retrieval)}</td></tr>
                <tr><th>deployed code</th><td class="mono">{H(ctx.Options.CodeCommit ?? "not supplied by deployment")}</td></tr>
                <tr><th>expected manifest set</th><td class="mono">{H(ctx.Options.ArtifactManifestId ?? "not supplied by deployment")}</td></tr>
                <tr><th>verified mounted manifest set</th><td class="mono">{H(ctx.Registry.VerifiedManifestSetId ?? "no signed manifests mounted")}</td></tr>
                <tr><th>immutable image</th><td class="mono">{H(ctx.Options.DeployImage ?? "not supplied by deployment")}</td></tr>
                </table></div>
                <h3>Mounted index identities</h3>
                <div class="dossier-table" tabindex="0" role="region" aria-label="Mounted index identities"><table>
                <tr><th>collection</th><th>works</th><th>versions</th><th>schema</th><th>corpus commit</th><th>stamp</th></tr>
                {coverageRows}
                </table></div>
                <p class="sub">Mounted total: {mounted.Sum(item => item.Coverage.Groups):n0} works and
                {mounted.Sum(item => item.Coverage.Versions):n0} dated versions. Artifact verification remains
                a distinct job on <a href="/verify">Verify artifacts</a>.</p>
                """;
        }

        string DeliveryRegistry()
        {
            var registry = ArchitectureProgram.Registry;
            var rows = string.Join("", registry.Milestones.Select(item =>
                $"<tr><td class=\"mono\">{H(item.Id)}</td><td><b>{H(item.Title)}</b><br>"
                + $"<span class=\"sub\">{H(item.Outcome)}</span></td><td>{StatusBadge(item.Status)}</td></tr>"));
            return $"""
                <h2 id="delivery-registry">Delivery registry</h2>
                <p>The target path is reviewed EU scope, official dated expressions, content-addressed
                text states, FTS5 keyword candidates plus local compact semantic candidates, temporal and
                hierarchy eligibility, fixed rank fusion, and the same typed result contracts. A capability
                is not described as live until its registry status and release evidence agree.</p>
                <div class="dossier-table" tabindex="0" role="region" aria-label="Architecture delivery milestones"><table>
                <tr><th>milestone</th><th>outcome</th><th>status</th></tr>{rows}
                </table></div>
                <p class="sub">Program <span class="mono">{H(registry.ProgramVersion)}</span>, updated
                <span class="mono">{H(registry.UpdatedAt)}</span>, review status
                <span class="mono">{H(registry.ReviewStatus)}</span>.</p>
                """;
        }

        string Addendum(string slug) => slug switch
        {
            "release" => MountedReleaseEvidence(),
            "limits" => DeliveryRegistry(),
            _ => "",
        };

        foreach (var definition in Pages)
        {
            var page = definition;
            app.MapGet(page.Path, () => Results.Content(Page(
                page.Label,
                Article(page, Addendum(page.Slug)),
                page.Subtitle,
                page.Path,
                page.Description), "text/html"));
        }

        app.MapGet("/architecture/dossier", () => Results.Content(Page(
            "Architecture dossier",
            FullArticle(Addendum),
            "The complete interview and print view, generated from the same nine source pages.",
            "/architecture/dossier",
            "The complete solution and AI architecture dossier for Lex.",
            noIndex: true), "text/html"));

        app.MapGet("/built/diagrams/{name}.svg", (string name) =>
        {
            if (!DiagramNames.Contains(name)) return Results.NotFound();
            var svg = ReadResource($"Lex.Web.architecture.diagrams.{name}.svg");
            return Results.Content(svg, "image/svg+xml", Encoding.UTF8);
        });

        return app;
    }
}
