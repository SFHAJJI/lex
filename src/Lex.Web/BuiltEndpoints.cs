using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
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
        new("repositories", "/built/repositories", "Repositories", "Five published repositories, and why evidence, dataset, product and release authority are kept apart.",
            "What each Lex repository holds and the separation of authority between them."),
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

    // The signed evaluation result leads the release page instead of trailing it. It was the last
    // section of fifteen, so the one fact a reader comes to check sat below every explanation of how
    // it is produced.
    private static string Article(DossierPage page, string lead, string addendum) =>
        $"{Tabs(page.Slug)}<article class=\"architecture-dossier\">"
        + lead
        + RenderMarkdown(MarkdownBySlug[page.Slug], full: false)
        + addendum
        + "</article>";

    private static string FullArticle(Func<string, string> lead, Func<string, string> addendum) =>
        "<article class=\"architecture-dossier architecture-dossier-full\">"
        + string.Join("", Pages.Select(page =>
            lead(page.Slug) + RenderMarkdown(MarkdownBySlug[page.Slug], full: true)
            + addendum(page.Slug)))
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

        // The verdict alone asks the reader to trust a boolean. The frozen questions and their
        // per-repetition scores are already inside the signed report, so the page shows them rather
        // than making a reader download JSON to learn what was actually asked.
        static string CaseOutcomeTable(VerifiedAssistantEvaluationEvidence evidence)
        {
            var rows = string.Join("", evidence.CaseOutcomes.Select(item =>
            {
                var contract = item.Passed == item.Repetitions
                    ? $"<span class=\"badge ok\">{item.Passed:n0} of {item.Repetitions:n0}</span>"
                    : $"<span class=\"badge\">{item.Passed:n0} of {item.Repetitions:n0}</span>";
                var relevance = string.Join(", ",
                    item.RelevanceScores.Select(score => $"{score:n0}/5"));
                return $"<tr><td class=\"mono\">{H(item.CaseId)}</td><td>{H(item.Question)}</td>"
                    + $"<td>{contract}</td><td class=\"mono\">{H(relevance)}</td></tr>";
            }));
            return $"""
                <h3>Every frozen case, and how it scored</h3>
                <p class="sub">Read from the signed report above. The contract column is the gate:
                it counts repetitions whose typed plan, arguments, outcomes, effects and latency all
                held. The relevance column is the separate grader's opinion of whether the answer
                addressed the question, recorded per repetition and gating nothing.</p>
                <div class="dossier-table" tabindex="0" role="region" aria-label="Signed assistant evaluation results by case"><table>
                <thead><tr><th>Case</th><th>Question</th><th>Contract</th><th>Relevance</th></tr></thead>
                <tbody>{rows}</tbody>
                </table></div>
                """;
        }

        static string EvaluationEvidenceCard(AssistantEvaluationEvidenceSnapshot snapshot)
        {
            if (snapshot.Evidence is not { } evidence)
                return """
                    <h2 id="latest-signed-evaluation">Latest signed assistant evaluation</h2>
                    <div class="notice"><b>No verified evaluation published.</b> This revision exposes no
                    cryptographically verified immutable evaluation package that matches its exact code,
                    image, artifact set, mounted indexes and signed catalog. Legal search and readiness do
                    not depend on this optional evidence view.</div>
                    <p class="sub">Machine-readable status: <a href="/built/release/evaluation.json">evaluation.json</a>.
                    Running identities: <a href="/attestation.json">attestation.json</a>.</p>
                    """;

            var candidateTokens = checked(evidence.CandidateInputTokens + evidence.CandidateOutputTokens);
            var graderTokens = checked(evidence.GraderInputTokens + evidence.GraderOutputTokens);
            var measuredCost = evidence.TotalCostEur.ToString("0.0000", CultureInfo.InvariantCulture);
            var maximumCost = evidence.MaximumCostEur.ToString("0.##", CultureInfo.InvariantCulture);
            return $"""
                <h2 id="latest-signed-evaluation">Latest signed assistant evaluation</h2>
                <div class="card">
                <p><span class="badge ok">Verified release</span> <b>Exact runtime match.</b>
                Signed report verdict: <b>passed</b>. Signed catalog: {evidence.CaseCount:n0} cases /
                {evidence.RepetitionCount:n0} repetitions. The canonical release verifier checked the
                case, budget and timing semantics before signing; this page authenticates that
                immutable report and its runtime bindings. The separate grader's relevance score is
                recorded per repetition in that report and gates nothing. No mutable
                <span class="mono">latest</span> pointer is trusted.</p>
                <div class="dossier-table" tabindex="0" role="region" aria-label="Latest signed assistant evaluation"><table class="kv">
                <tr><th>evaluated at</th><td class="mono">{H(evidence.RunAt)}</td></tr>
                <tr><th>running and evaluated revision</th><td class="mono">{H(evidence.Revision)}</td></tr>
                <tr><th>running and evaluated code commit</th><td class="mono">{H(evidence.CodeCommit)}</td></tr>
                <tr><th>running and evaluated image</th><td class="mono">{H(evidence.Image)}</td></tr>
                <tr><th>running and evaluated artifact set</th><td class="mono">{H(evidence.ArtifactManifestSet)}</td></tr>
                <tr><th>signed catalog SHA-256</th><td class="mono">{H(evidence.CatalogSha256)}</td></tr>
                <tr><th>signed report SHA-256</th><td class="mono">{H(evidence.ReportSha256)}</td></tr>
                <tr><th>candidate model</th><td>{H(evidence.CandidateModelName)} {H(evidence.CandidateModelVersion)}
                <span class="mono">({H(evidence.CandidateDeployment)})</span></td></tr>
                <tr><th>release grader</th><td>{H(evidence.GraderModelName)} {H(evidence.GraderModelVersion)}
                <span class="mono">({H(evidence.GraderDeployment)})</span></td></tr>
                <tr><th>measured tokens</th><td class="mono">candidate {candidateTokens:n0}; grader {graderTokens:n0}</td></tr>
                <tr><th>measured cost</th><td class="mono">EUR {measuredCost} / EUR {maximumCost} maximum</td></tr>
                <tr><th>measured latency</th><td class="mono">first result p95 {evidence.FirstOperationP95Milliseconds:n0} ms;
                total p99 {evidence.TotalP99Milliseconds:n0} ms; browser presentation p95 {evidence.BrowserP95Milliseconds:n0} ms</td></tr>
                </table></div>
                {CaseOutcomeTable(evidence)}
                <p><a href="{H(evidence.ReleaseUrl)}">Immutable GitHub release</a> ·
                <a href="{H(evidence.ReportUrl)}">report</a> ·
                <a href="{H(evidence.ManifestUrl)}">signed manifest</a> ·
                <a href="{H(evidence.SignatureUrl)}">signature</a> ·
                <a href="/built/release/evaluation.json">public status JSON</a> ·
                <a href="/attestation.json">running attestation</a></p>
                </div>
                <p class="sub">These hashes intentionally differ: the 40-character Git commit identifies
                source code; the artifact-set, catalog and report values are separate SHA-256 identities.
                Verification on this page means the signed report embeds the former identities exactly and
                its own full digest matches the release tag; it does not rerun or independently rescore the
                evaluation.</p>
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

        static string Lead(string slug, AssistantEvaluationEvidenceSnapshot evaluation) => slug switch
        {
            "release" => EvaluationEvidenceCard(evaluation),
            _ => "",
        };

        string Addendum(string slug, AssistantEvaluationEvidenceSnapshot evaluation) => slug switch
        {
            "release" => MountedReleaseEvidence(),
            "limits" => DeliveryRegistry(),
            _ => "",
        };

        foreach (var definition in Pages)
        {
            var page = definition;
            app.MapGet(page.Path, async (HttpContext http,
                IAssistantEvaluationEvidenceProvider evaluations,
                CancellationToken cancellationToken) =>
            {
                var evidence = page.Slug == "release"
                    ? await evaluations.GetAsync(cancellationToken)
                    : AssistantEvaluationEvidenceSnapshot.Unavailable;
                if (page.Slug == "release") http.Response.Headers.CacheControl = "no-store";
                return Results.Content(Page(
                    page.Label,
                    Article(page, Lead(page.Slug, evidence), Addendum(page.Slug, evidence)),
                    page.Subtitle,
                    page.Path,
                    page.Description), "text/html");
            });
        }

        app.MapGet("/architecture/dossier", async (HttpContext http,
            IAssistantEvaluationEvidenceProvider evaluations,
            CancellationToken cancellationToken) =>
        {
            var evidence = await evaluations.GetAsync(cancellationToken);
            http.Response.Headers.CacheControl = "no-store";
            return Results.Content(Page(
                "Architecture dossier",
                FullArticle(slug => Lead(slug, evidence), slug => Addendum(slug, evidence)),
                "The complete interview and print view, generated from the same nine source pages.",
                "/architecture/dossier",
                "The complete solution and AI architecture dossier for Lex.",
                noIndex: true), "text/html");
        });

        app.MapGet("/built/release/evaluation.json", async (HttpContext http,
            IAssistantEvaluationEvidenceProvider evaluations,
            CancellationToken cancellationToken) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var snapshot = await evaluations.GetAsync(cancellationToken);
            var json = new JsonObject
            {
                ["schema"] = "lex-public-assistant-evaluation-status/1",
                ["status"] = snapshot.Verified ? "verified_signed_release" : "unavailable",
            };
            if (snapshot.Evidence is { } evidence)
            {
                json["release_tag"] = evidence.ReleaseTag;
                json["release_url"] = evidence.ReleaseUrl;
                json["run_at"] = evidence.RunAt;
                json["candidate_revision"] = evidence.Revision;
                json["code_commit"] = evidence.CodeCommit;
                json["image"] = evidence.Image;
                json["report_sha256"] = evidence.ReportSha256;
                json["artifact_manifest_set"] = evidence.ArtifactManifestSet;
                json["index_manifest_ids"] = new JsonArray(evidence.IndexManifestIds
                    .Order(StringComparer.Ordinal).Select(value => JsonValue.Create(value)).ToArray());
                json["catalog_sha256"] = evidence.CatalogSha256;
                json["signed_report_verdict"] = "passed";
                json["cases"] = evidence.CaseCount;
                json["repetitions"] = evidence.RepetitionCount;
                json["candidate_model"] = new JsonObject
                {
                    ["host"] = evidence.CandidateModelHost,
                    ["deployment"] = evidence.CandidateDeployment,
                    ["name"] = evidence.CandidateModelName,
                    ["version"] = evidence.CandidateModelVersion,
                };
                json["grader_model"] = new JsonObject
                {
                    ["deployment"] = evidence.GraderDeployment,
                    ["name"] = evidence.GraderModelName,
                    ["version"] = evidence.GraderModelVersion,
                };
                json["usage"] = new JsonObject
                {
                    ["candidate_input_tokens"] = evidence.CandidateInputTokens,
                    ["candidate_output_tokens"] = evidence.CandidateOutputTokens,
                    ["grader_input_tokens"] = evidence.GraderInputTokens,
                    ["grader_output_tokens"] = evidence.GraderOutputTokens,
                };
                json["latency_milliseconds"] = new JsonObject
                {
                    ["first_operation_p95"] = evidence.FirstOperationP95Milliseconds,
                    ["total_p99"] = evidence.TotalP99Milliseconds,
                    ["browser_p95"] = evidence.BrowserP95Milliseconds,
                };
                json["actual_cost_eur"] = evidence.TotalCostEur;
                json["maximum_cost_eur"] = evidence.MaximumCostEur;
            }
            return Results.Content(json.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
            }), "application/json");
        });

        app.MapGet("/built/diagrams/{name}.svg", (string name) =>
        {
            if (!DiagramNames.Contains(name)) return Results.NotFound();
            var svg = ReadResource($"Lex.Web.architecture.diagrams.{name}.svg");
            return Results.Content(svg, "image/svg+xml", Encoding.UTF8);
        });

        return app;
    }
}
