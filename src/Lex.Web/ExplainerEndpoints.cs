using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using Lex.Index;
using Lex.Mcp;
using static Lex.Web.PageShell;
using static Lex.Web.Fragments;

namespace Lex.Web;

/// <summary>
/// The pages that explain the system rather than serve it: how it works, how it was built, the decisions and what each one cost, how to verify a build yourself, and how to point your own model at it. Static content over the mounted indexes, no request state.
/// </summary>
public static class ExplainerEndpoints
{
    public static IEndpointRouteBuilder MapExplainers(this IEndpointRouteBuilder app, WebContext ctx)
    {
        // Re-declared here so every moved route body is byte-identical to what it was in
        // Program.cs. That is the property the golden snapshots check.
        string Page(string title, string body, string? subtitle = null, string nav = "",
                    string? h1 = null, string? canonicalPath = null, string? jsonLd = null,
                    string? description = null, string? lang = null, bool assistant = false)
            => PageShell.Page(ctx.PublicBase, title, body, subtitle, nav, h1, canonicalPath,
                              jsonLd, description, lang, ctx.Options.CodeCommit, assistant);
        var readers = ctx.Registry.All;
        var publicBase = ctx.PublicBase;
        var mcpCore = ctx.Mcp;
        var architecture = ArchitectureProgram.Registry;
        var retrievalCases = LoadRetrievalCases();
        var retrievalBaseline = LoadRetrievalBaseline();

        string ArchitectureTabs(string active)
        {
            string Tab(string id, string href, string label) =>
                $"<a class=\"{(active == id ? "badge ok" : "badge")}\" href=\"{href}\">{label}</a>";
            return $"""
                <nav class="tabs" aria-label="Architecture evidence" style="display:flex;gap:8px;flex-wrap:wrap;margin:0 0 22px">
                  {Tab("current", "/architecture", "Current")}
                  {Tab("next", "/architecture/next", "Next")}
                  {Tab("decisions", "/decisions", "Decisions")}
                  {Tab("benchmarks", "/benchmarks", "Benchmarks")}
                </nav>
                """;
        }

        static string StatusBadge(string status) =>
            $"<span class=\"badge{(status == "shipped" ? " ok" : status == "gated" ? " warn" : "")}\">{H(status)}</span>";

        app.MapGet("/ai", () => Results.Redirect("/developers#assistant", permanent: true));

        app.MapGet("/architecture", () =>
        {
            var cov = readers.Values.Select(r => r.Coverage()).OrderBy(c => c.Collection).ToList();
            var current = architecture.Current;
            var mountedSchemas = string.Join(", ", cov.Select(c => c.Stamp.GetValueOrDefault("schema", "unknown"))
                                                     .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            var hybridCollections = ctx.Registry.Values.Where(reader => reader.HybridReady)
                .Select(reader => reader.Collection).Order(StringComparer.Ordinal).ToList();
            var liveRetrieval = hybridCollections.Count == 0
                ? $"{current.Retrieval}, deterministic FTS5/BM25"
                : $"keyword default; local hybrid preview available (gated) on {string.Join(", ", hybridCollections)}";
            var coverageRows = string.Join("", cov.Select(c => $"""
                <tr><td>{H(c.Collection)}</td><td class="mono">{c.Groups:n0}</td>
                <td class="mono">{c.Versions:n0}</td><td class="mono">{H(c.Stamp.GetValueOrDefault("schema"))}</td>
                <td class="mono">{H(c.Stamp.GetValueOrDefault("corpus_commit"))}</td></tr>
                """));
            var body = ArchitectureTabs("current") + $"""
                <p class="lede">This page describes the system serving requests now. Target-state work is kept
                separately on <a href="/architecture/next">Next</a>.</p>
                <div class="card"><table class="kv">
                <tr><th>retrieval</th><td>{H(liveRetrieval)}</td></tr>
                <tr><th>hosting</th><td>{H(current.Hosting)}, {H(current.Region)}</td></tr>
                <tr><th>resources</th><td>{H(current.Resource)}, {H(current.Scale)}</td></tr>
                <tr><th>structured UI contract</th><td>{H(current.StructuredContract)}</td></tr>
                <tr><th>comparison contract</th><td>{H(current.ComparisonContract)}</td></tr>
                <tr><th>deployment observation</th><td class="mono">{H(current.ObservedAt)}</td></tr>
                <tr><th>deployed code</th><td class="mono">{H(ctx.Options.CodeCommit ?? "not supplied by deployment")}</td></tr>
                <tr><th>artifact manifest set</th><td class="mono">{H(ctx.Options.ArtifactManifestId ?? "not supplied by deployment")}</td></tr>
                <tr><th>immutable image</th><td class="mono">{H(ctx.Options.DeployImage ?? "not supplied by deployment")}</td></tr>
                </table></div>
                <h2>Mounted coverage, read live</h2>
                <div class="card"><table tabindex="0" aria-label="Mounted index collections"><tr><th>collection</th><th>works</th><th>versions</th><th>schema</th><th>corpus commit</th></tr>
                {coverageRows}</table></div>
                <h2>Contracts preserved</h2>
                <p>Exact publisher text, hashes, anchors, timelines, refusals, comparisons and diffs remain
                authoritative. The backend returns structured MCP JSON and the workspace renders the separate
                <span class="mono">UiEffect</span> field.</p>
                """ + """
                <p>Lex answers one question, <b>what did the rule say on that date?</b>, for Luxembourg law and the reviewed-scope EU works in the mounted index,
                in a way a developer can build on and an auditor can check. Everything below is open source and open data.</p>

                <h2>Two layers, one hash chain</h2>
                <div class="card"><pre class="mono" style="white-space:pre-wrap;font-size:12.5px;margin:0">EVIDENCE LAYER (append-only, verbatim)          CONSUMPTION LAYER (regenerable, clean)
                lex-corpus-lu-legilux   lex-corpus-eu-eurlex   lex-articles
                the exact bytes the state published       →   per-ARTICLE Markdown + JSON
                sha256 per file, observation chains            publisher anchors + measured continuity gate
                                                               publisher timeline intervals per provision
                             deterministic, versioned,          per-anchor history + renumbering events
                             IMMUTABLE extraction profiles          │
                             (akn-lu/1, xhtml-eu/1, code,          ▼
                              never an LLM)                    signed SQLite indexes (MOUNTED_INDEX_SCHEMAS)
                                                               provisions + FTS + time axis, ECDSA-P256 stamp
                                                                    │
                                    this site · /mcp (any MCP client) · datasets</pre></div>

                <p>Every provision's <span class="mono">text_sha256</span> chains to a verbatim-file sha256 in the evidence
                repo: re-run the pinned open-source extractor on the state's bytes and you get these bytes.
                <a href="/verify">Verify it yourself</a>, the defence is never "trust Lex".</p>

                <h2>The retrieval unit is the article</h2>
                <p>Search hits, <span class="mono">as_of</span> (with <span class="mono">outline</span> and
                <span class="mono">select</span> modes), and the <span class="mono">article_history</span> tool all operate
                per provision. "What did Article 92 say over its life?" is a file read: every distinct text on its
                publisher timeline, plus mechanically detected renumberings (identical-hash matching, never interpretation).</p>

                <h2>Honesty as an API contract</h2>
                <div class="card"><table>
                <tr><th>refusal status</th><th>meaning</th></tr>
                <tr><td class="mono">no_version_for_date</td><td>the work exists; no publisher version covers that date</td></tr>
                <tr><td class="mono">unknown_work / unknown_anchor</td><td>Lex does not hold it, and says so</td></tr>
                <tr><td class="mono">anchor_not_in_version</td><td>that article did not exist in that version (knowing this IS the product)</td></tr>
                <tr><td class="mono">text_withheld</td><td>metadata held, text gate not cleared; official link provided</td></tr>
                <tr><td class="mono">text_not_available</td><td>publisher record held; no safely derived provision text; official link provided</td></tr>
                <tr><td class="mono">no_provision_history</td><td>the work is held without per-article history</td></tr>
                </table></div>
                <p class="sub">MCP MCP_SERVER_VERSION uses a closed status vocabulary. See the
                <a href="https://github.com/SFHAJJI/lex/blob/main/docs/mcp-2-migration.md" rel="noopener">migration note</a>.</p>
                <p>A flagged wrong answer is still wrong, so Lex refuses instead; <a href="/coverage">coverage</a> exists to
                state what we do <b>not</b> have. The AI layer (<a href="/">the front page</a>) is additive and separated:
                a bounded retrieval loop uses the same in-process tool core the public
                <span class="mono">/mcp</span> serves, then Agent Framework composes claim-typed evidence
                and conditionally judges grounded prose. Application code, not the model, owns work
                resolution, tool authorization, citations, typed gaps and legal text (fitness rule F10).</p>

                <h2>Build on it</h2>
                <p>
                <a href="https://github.com/SFHAJJI/lex-articles">lex-articles</a>, machine-readable corpus (CC-BY, SCHEMA.md contract) ·
                <a href="https://github.com/SFHAJJI/lex">lex</a>, all code, Apache-2.0, incl. the
                <a href="https://github.com/SFHAJJI/lex/blob/main/docs/lex-spec-v4.md">full decision record (D1, D47)</a> ·
                <a href="https://github.com/SFHAJJI/lex-corpus-lu-legilux">evidence repos</a> ·
                hosted MCP: <span class="mono">claude mcp add --transport http lex https://law.soufien.lu/mcp</span></p>
                """;
            body = body.Replace("MOUNTED_INDEX_SCHEMAS", H(mountedSchemas), StringComparison.Ordinal);
            body = body.Replace("MCP_SERVER_VERSION", H(McpSdkBridge.ServerVersion), StringComparison.Ordinal);
            return Results.Content(Page("Architecture", body,
                "what is deployed now, read separately from what comes next",
                canonicalPath: "/architecture"), "text/html");
        });

        app.MapGet("/architecture/next", () =>
        {
            var rows = string.Join("", architecture.Milestones.Select(m => $"""
                <tr><td class="mono">{H(m.Id)}</td><td><b>{H(m.Title)}</b><br><span class="sub">{H(m.Outcome)}</span></td>
                <td>{StatusBadge(m.Status)}</td></tr>
                """));
            var body = ArchitectureTabs("next") + $"""
                <p class="lede">The accepted target architecture, with status read from the registry committed
                beside the implementation. Only milestones marked shipped are live; gated and planned work is not.</p>
                <div class="card"><table tabindex="0" aria-label="Architecture delivery milestones"><tr><th>milestone</th><th>outcome</th><th>status</th></tr>{rows}</table></div>
                <h2>Target path</h2>
                <div class="card"><pre class="mono" style="white-space:pre-wrap;margin:0;font-size:13px">Reviewed EU scope configuration
                  -&gt; every official dated FR/EN expression plus bounded legal relationships
                  -&gt; content-addressed text states and occurrence mappings
                  -&gt; FTS5 keyword candidates plus local compact semantic candidates
                  -&gt; date and hierarchy eligibility
                  -&gt; fixed reciprocal rank fusion
                  -&gt; the same exact provision JSON, timeline, comparison and UiEffect contracts</pre></div>
                <p>Hybrid retrieval remains gated until its public relevance, temporal, latency and memory
                thresholds pass. Missing official consolidation remains a named gap, never generated wording.</p>
                <p class="sub">Program <span class="mono">{H(architecture.ProgramVersion)}</span>, updated
                <span class="mono">{H(architecture.UpdatedAt)}</span>, review status
                <span class="mono">{H(architecture.ReviewStatus)}</span>.</p>
                """;
            return Results.Content(Page("Next architecture", body,
                "the accepted target, its gates, and what has actually shipped",
                canonicalPath: "/architecture/next"), "text/html");
        });

        app.MapGet("/benchmarks", () =>
        {
            static string F(double value, string format) =>
                value.ToString(format, CultureInfo.InvariantCulture);
            var b = architecture.Baseline;
            var expectedCollections = retrievalCases.Select(item => item.Collection)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var reports = expectedCollections.Select(collection =>
                    (Collection: collection, Report: LoadReport(collection)))
                .Where(item => item.Report is not null)
                .ToDictionary(item => item.Collection, item => item.Report!, StringComparer.Ordinal);
            var compatible = RetrievalBenchmarkGate.ReportsAreCompatible(
                reports.Values.ToArray(), expectedCollections.Length);
            var combinedPassed = compatible && reports.Values.All(item => item.ActivationGatePassed);
            var reportEntry = reports.OrderBy(item => item.Key, StringComparer.Ordinal).FirstOrDefault();
            var report = reportEntry.Value;
            var caseRows = string.Join("", retrievalCases.GroupBy(c => c.Category)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"<tr><td>{H(g.Key)}</td><td class=\"mono\">{g.Count()}</td></tr>"));
            var relevance = report is null
                ? $"""
                  <div class="notice"><b>Not measured yet.</b> The public {retrievalCases.Count}-case suite is a gated milestone.
                  Hybrid will not become default until exact identifiers, temporal isolation, nDCG@10,
                  regression, latency and memory pass their recorded thresholds.</div>
                  """
                : $"""
                  <div class="notice"><b>Latest combined gate: {(combinedPassed ? "passed" : "not passed")}.</b>
                  Measured collections: {reports.Count:n0}/{expectedCollections.Length:n0}.
                  Reports must share case digest, code, model and resource configuration, and each must name
                  its verified publisher release manifest, before this can pass.
                  Detail below: {H(reportEntry.Key)}, review status {H(report.ReviewStatus)}, measured
                  {H(report.Timestamp)} over {report.SampleCount:n0} cases.</div>
                  <div class="card"><table>
                  <tr><th>measure</th><th>keyword</th><th>hybrid</th></tr>
                  <tr><td>tuning MRR</td><td class="mono">{F(report.KeywordTuning.Mrr, "0.000")}</td><td class="mono">{F(report.HybridTuning.Mrr, "0.000")}</td></tr>
                  <tr><td>tuning Recall@10</td><td class="mono">{F(report.KeywordTuning.RecallAt10, "0.000")}</td><td class="mono">{F(report.HybridTuning.RecallAt10, "0.000")}</td></tr>
                  <tr><td>tuning nDCG@10</td><td class="mono">{F(report.KeywordTuning.NdcgAt10, "0.000")}</td><td class="mono">{F(report.HybridTuning.NdcgAt10, "0.000")}</td></tr>
                  <tr><td>holdout nDCG@10</td><td class="mono">{F(report.KeywordHoldout.NdcgAt10, "0.000")}</td><td class="mono">{F(report.HybridHoldout.NdcgAt10, "0.000")}</td></tr>
                  <tr><td>holdout no-hit accuracy</td><td class="mono">{F(report.KeywordHoldout.NoHitAccuracy, "0.000")}</td><td class="mono">{F(report.HybridHoldout.NoHitAccuracy, "0.000")}</td></tr>
                  <tr><td>holdout resolution accuracy</td><td class="mono">{F(report.KeywordHoldout.ResolutionAccuracy, "0.000")}</td><td class="mono">{F(report.HybridHoldout.ResolutionAccuracy, "0.000")}</td></tr>
                  <tr><td>holdout warm p95</td><td class="mono">{F(report.KeywordHoldout.P95Ms, "0.0")} ms</td><td class="mono">{F(report.HybridHoldout.P95Ms, "0.0")} ms</td></tr>
                  <tr><td>holdout warm p99</td><td class="mono">{F(report.KeywordHoldout.P99Ms, "0.0")} ms</td><td class="mono">{F(report.HybridHoldout.P99Ms, "0.0")} ms</td></tr>
                  <tr><td>tuning warm p95</td><td class="mono">{F(report.KeywordTuning.P95Ms, "0.0")} ms</td><td class="mono">{F(report.HybridTuning.P95Ms, "0.0")} ms</td></tr>
                  <tr><td>tuning warm p99</td><td class="mono">{F(report.KeywordTuning.P99Ms, "0.0")} ms</td><td class="mono">{F(report.HybridTuning.P99Ms, "0.0")} ms</td></tr>
                  </table><p class="sub">Code <span class="mono">{H(report.CodeCommit)}</span>, corpus
                  <span class="mono">{H(report.CorpusCommit)}</span>, manifest <span class="mono">{H(report.ManifestId)}</span>,
                  model <span class="mono">{H(report.ModelId)}@{H(report.ModelRevision)}</span>.<br>
                  Resource: {H(report.ResourceConfiguration)}. Working set {F(report.ProcessMemoryBytes / 1048576d, "0.0")} MiB;
                  index {F(report.IndexBytes / 1048576d, "0.0")} MiB; vectors {F(report.VectorBytes / 1048576d, "0.0")} MiB.
                  Gate failures: {H(report.GateFailures.Count == 0 ? "none" : string.Join("; ", report.GateFailures))}.</p></div>
                  <p><a href="/benchmarks/latest.json">Download the complete latest benchmark report</a>.</p>
                  """;
            var body = ArchitectureTabs("benchmarks") + $"""
                <p class="lede">Evidence is published with identity and context. A missing measurement is
                displayed as missing rather than replaced with an estimate.</p>
                <h2>Current service baseline</h2>
                <div class="card"><table class="kv">
                <tr><th>kind</th><td>{H(b.Kind)}</td></tr>
                <tr><th>measured</th><td class="mono">{H(b.MeasuredAt)}</td></tr>
                <tr><th>code commit</th><td class="mono">{H(b.CodeCommit)}</td></tr>
                <tr><th>live corpus commits</th><td class="mono">LU {H(b.LiveLuCorpusCommit)}, EU {H(b.LiveEuCorpusCommit)}</td></tr>
                <tr><th>sampled MCP requests, 7 days</th><td class="mono">{b.McpRequests7dSampled:n0}</td></tr>
                <tr><th>internal latency</th><td class="mono">p50 {F(b.McpInternalP50Ms, "0.00")} ms, p95 {F(b.McpInternalP95Ms, "0.00")} ms, p99 {F(b.McpInternalP99Ms, "0.00")} ms</td></tr>
                <tr><th>average working set</th><td class="mono">{b.AverageWorkingSetMib:n0} MiB</td></tr>
                </table><p class="sub">{H(b.Note)}</p></div>
                <h2>Retrieval relevance</h2>
                {relevance}
                <div class="card"><table><tr><th>case category</th><th>public judgments</th></tr>
                {caseRows}</table></div>
                <p><a href="/benchmarks/cases.json">Download all {retrievalCases.Count} public cases and judgments</a>.
                Each case names its collection, tuning or holdout split, language, time scope,
                canonical collection/work identities, expected intent state, explanation and review status.</p>
                <h2>Publication rule</h2>
                <p>Future reports name code and corpus commits, the signed artifact manifest, embedding model,
                machine or Azure resource, timestamp, sample count and review status. The public labels are
                engineer-reviewed retrieval judgments, not legal conclusions. Tuning and holdout results are
                reported separately, and only the holdout can authorize a default change.</p>
                """;
            return Results.Content(Page("Benchmarks", body,
                "measured retrieval, latency, memory, index size and cost evidence",
                canonicalPath: "/benchmarks"), "text/html");
        });

        app.MapGet("/benchmarks/latest.json", () =>
        {
            var collections = retrievalCases.Select(item => item.Collection)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var reports = collections.Select(collection =>
                    (Collection: collection, Report: LoadReport(collection)))
                .Where(item => item.Report is not null).ToArray();
            if (reports.Length == 0) return Results.NotFound(new { status = "not_measured_yet" });
            var compatible = RetrievalBenchmarkGate.ReportsAreCompatible(
                reports.Select(item => item.Report!).ToArray(), collections.Length);
            return Results.Json(new
            {
                schema = "lex-retrieval-benchmark-set/1",
                activation_gate_passed = compatible && reports.All(item => item.Report!.ActivationGatePassed),
                expected_collections = collections,
                reports = reports.ToDictionary(item => item.Collection, item => item.Report),
            });
        });

        app.MapGet("/benchmarks/cases.json", () => Results.Json(
            retrievalCases, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
                WriteIndented = true,
            }));

        RetrievalBenchmarkReport? LoadReport(string collection)
        {
            var path = Path.Combine(ctx.Options.IndexDir, $"retrieval-benchmark-{collection}.json");
            try
            {
                if (!File.Exists(path) || !ctx.Registry.IsArtifactVerified(Path.GetFileName(path)))
                    return null;
                var report = JsonSerializer.Deserialize<RetrievalBenchmarkReport>(File.ReadAllBytes(path),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
                var expectedCases = retrievalCases.Count(item => item.Collection == collection);
                var expectedTuning = retrievalCases.Count(item => item.Collection == collection
                    && item.Split == "tuning");
                var expectedHoldout = retrievalCases.Count(item => item.Collection == collection
                    && item.Split == "holdout");
                return report is not null
                       && report.Schema == "lex-retrieval-benchmark/3"
                       && report.BaselineSchema == retrievalBaseline.Schema
                       && report.SampleCount == expectedCases
                       && report.TuningSampleCount == expectedTuning
                       && report.HoldoutSampleCount == expectedHoldout
                       && report.KeywordTuning is not null
                       && report.HybridTuning is not null
                       && report.KeywordHoldout is not null
                       && report.HybridHoldout is not null
                       && report.GateFailures is not null
                       && report.ActivationGatePassed == (report.GateFailures.Count == 0)
                       && RetrievalBenchmarkGate.HasReleaseIdentity(report)
                       && BenchmarkClaimsMatchVerifiedManifests(
                           report, collection, ctx.Registry.VerifiedArtifactManifests)
                       && report.ReviewAttestation
                           == $"{retrievalBaseline.ReviewedBy}@{retrievalBaseline.ReviewedAt}"
                       && string.Equals(report.ExpectedCasesSha256, retrievalBaseline.CasesSha256,
                           StringComparison.OrdinalIgnoreCase)
                       && string.Equals(report.ActualCasesSha256, retrievalBaseline.CasesSha256,
                           StringComparison.OrdinalIgnoreCase)
                    ? report : null;
            }
            catch (Exception)
            {
                // Benchmark evidence is fail-closed and never a startup dependency.
                return null;
            }
        }

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
            var manifests = new JsonArray();
            foreach (var manifest in ctx.Registry.VerifiedArtifactManifests)
                manifests.Add(new JsonObject
                {
                    ["file"] = manifest.File,
                    ["sha256"] = manifest.Sha256,
                    ["key_id"] = manifest.KeyId,
                    ["code_commit"] = manifest.CodeCommit,
                    ["created_at"] = manifest.CreatedAt,
                    ["artifacts"] = new JsonArray(manifest.Artifacts.Select(path => JsonValue.Create(path)).ToArray()),
                });
            return Results.Content(new JsonObject
            {
                ["what"] = "attestation of every verified release manifest and embedded index stamp this deployment serves",
                ["artifact_trust"] = "whole-artifact manifests are verified against public-key fingerprints pinned in the application release",
                ["artifact_signature_format"] = "ECDSA-P256-SHA256, IEEE P1363 (r||s, 64 bytes), base64",
                ["artifact_signature_binds"] = "the canonical lex-artifacts/1 manifest bytes; its file entries bind every artifact path, size and sha256",
                ["embedded_stamp_signature_binds"] = "the canonical stamp text: every stamp field except signature/public_key, sorted by key, joined as k=v lines",
                // Compatibility keys retained for clients that consumed the original stamp-only attestation.
                ["signature_binds"] = "the canonical stamp text: every stamp field except signature/public_key, sorted by key, joined as k=v lines",
                ["signature_format"] = "ECDSA-P256-SHA256, IEEE P1363 (r||s, 64 bytes), base64",
                ["verify"] = "see /verify",
                ["served_at"] = ctx.Clock.GetUtcNow().ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["deployment"] = new JsonObject
                {
                    ["code_commit"] = ctx.Options.CodeCommit,
                    ["artifact_manifest_set"] = ctx.Options.ArtifactManifestId,
                    ["image"] = ctx.Options.DeployImage,
                },
                ["artifact_manifests"] = manifests,
                ["collections"] = collections,
            }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), "application/json");
        });

        app.MapGet("/verify", () =>
        {
            var verifiedManifestCount = ctx.Registry.VerifiedArtifactManifests.Count;
            var manifestNoun = verifiedManifestCount == 1 ? "manifest" : "manifests";
            var artifactStatus = verifiedManifestCount > 0
                ? $"""
                  <div class="notice"><b>Verified now.</b> This deployment mounted
                  {verifiedManifestCount:n0} signed artifact {manifestNoun}. The exact hashes and key ids are in
                  <a href="/attestation.json">attestation.json</a>.</div>
                  """
                : """
                  <div class="notice"><b>Migration state.</b> This deployment has no verified whole-artifact
                  manifest mounted. Its embedded index stamps are public provenance only; do not treat them as
                  independent artifact trust.</div>
                  """;
            var body = $$"""
                {{artifactStatus}}
                <p>Production trust begins with a <b>whole-artifact manifest</b>, not with a key downloaded
                beside the artifact it is meant to verify. Each manifest names the index, vectors, embedding
                model, tokenizer, scope and benchmark files in that release, with every file's sha256 and size.
                Azure Key Vault signs the canonical manifest with ECDSA P-256.</p>

                <h2>The runtime trust decision</h2>
                <p>The application release pins the accepted signing-key fingerprint in
                <span class="mono">deploy/trusted-artifact-roots.json</span>. Image construction verifies the
                manifest signature against that pinned root, then verifies every listed file before it can enter
                the image. Startup performs the same check before mounting an index. A missing, changed or
                incorrectly signed file fails closed, and the previous verified Container Apps revision keeps
                serving traffic.</p>

                <h2>Verify the deployed release independently</h2>
                <p><a href="/attestation.json">attestation.json</a> reports the exact deployed code commit and
                manifest hashes. Check out that commit, rather than today's branch tip, because its trust-root file
                is the one the running image actually pinned. Then download the public release assets and run the
                same verifier:</p>
                <div class="card"><pre class="mono" style="white-space:pre-wrap">curl -fsS https://law.soufien.lu/attestation.json -o attestation.json
                code=$(jq -er '.deployment.code_commit' attestation.json)
                git clone https://github.com/SFHAJJI/lex
                git -C lex checkout "$code"

                for repo in lex-corpus-lu-legilux lex-corpus-eu-eurlex; do
                  mkdir -p "release/$repo"
                  gh release download --repo "SFHAJJI/$repo" --dir "release/$repo"
                  for manifest in "release/$repo"/*.manifest.json; do
                    dotnet run --project lex/src/Lex.Ingest -- artifact verify \
                      --root "release/$repo" --manifest "$manifest" \
                      --signature "${manifest%.json}.sig" \
                      --trust-roots lex/deploy/trusted-artifact-roots.json
                  done
                done</pre></div>
                <p class="sub">The verifier checks the signature, key id, pinned fingerprint, every declared path,
                file size and sha256. Compare each manifest's sha256 with the
                <span class="mono">artifact_manifests</span> array in <span class="mono">attestation.json</span>
                to prove that the downloaded release is the one this deployment serves.</p>

                <h2>The embedded stamp is provenance, not the trust root</h2>
                <p>Each SQLite index still carries a signed compatibility stamp binding its schema, corpus commit,
                build time, attribution, NOTICE and corpus statistics. The public key exposed at
                <a href="/pubkey.pem">pubkey.pem</a> comes from that stamp. It can demonstrate that the stamp is
                internally consistent, but a key supplied beside its own signature cannot establish who should be
                trusted. Runtime authorization therefore comes only from the separately pinned manifest root.</p>

                <h2>What the signatures do, and do not, prove</h2>
                <p>The manifest proves that the holder of the pinned release key signed these exact artifacts. It
                does <b>not</b> by itself prove that legal wording matches the publisher. That is what the open hash
                chain is for: every provision's <span class="mono">text_sha256</span> derives deterministically from
                a verbatim publisher file whose sha256 is recorded in the corpus repositories. Re-run the pinned
                extractor on the same evidence and compare the result; the defence is never merely "trust Lex".</p>

                <h2>Verify a citation against the state's bytes</h2>
                <p>Clone the evidence repo and the code, then re-derive offline:</p>
                <div class="card"><pre class="mono" style="white-space:pre-wrap">git clone https://github.com/SFHAJJI/lex &amp;&amp; git clone https://github.com/SFHAJJI/lex-corpus-lu-legilux
                cd lex &amp;&amp; dotnet run --project src/Lex.Ingest -- verify derive --publisher lu-legilux --corpus ../lex-corpus-lu-legilux --articles ../lex-articles</pre></div>
                <p class="sub">Extraction profiles are immutable (<span class="mono">akn-lu/1</span>, <span class="mono">akn-lu-document/1</span>,
                <span class="mono">pdf-memorial-lu/2</span>, <span class="mono">xhtml-eu/1</span>);
                a citation pinned under a profile verifies under that profile, forever. Contract:
                <a href="https://github.com/SFHAJJI/lex-articles/blob/main/SCHEMA.md">SCHEMA.md</a>.</p>
                """;
            return Results.Content(Page("Verify", body,
                "the signature, the hash chain, and how to check both without trusting us",
                canonicalPath: "/verify"), "text/html");
        });

        // ---- /built: the engineering story. Written for someone deciding whether the person who
        // built this can build things — decisions, tradeoffs, failures, and how correctness is proven.
        app.MapGet("/built", () =>
        {
            var cov = readers.Values.Select(r => r.Coverage()).ToList();
            var body = $"""
                <p class="lede">A point-in-time legal database, an MCP server, a grounded assistant and a
                nightly pipeline, built solo. This page is the part usually left out: the decisions, the
                things that broke, and how correctness is actually proven rather than asserted.</p>

                <div class="notice"><b>Built with AI assistance.</b> The architecture, the decisions and the
                verification are mine; a great deal of the code was written with an AI pair. That is stated
                here because it is true, and because the parts that matter, the decision record, the failure
                modes below, and the tests that catch them, are where the engineering actually lives.</div>

                <h2>The problem</h2>
                <p>Ask any legal site what a law says and you get today's text. Almost every question that
                matters is about a <b>date</b>: what applied when the contract was signed, when the fine was
                issued, when the breach happened. Official publishers do hold dated consolidated editions,
                but scattered across formats (Akoma Ntoso XML, Formex XML, legacy XHTML), with no article-level
                access and no machine interface. Lex turns that into one queryable, verifiable history.</p>

                <h2>The shape of it</h2>
                <div class="card"><pre class="mono" style="white-space:pre-wrap;margin:0;font-size:13px">
                official publishers          nightly, one scheduled job
                Legilux · EUR-Lex/Cellar     ──────────────────────────────────
                        │                    ingest → anomaly gate → derive →
                        │  verbatim bytes      (&gt;5% drop =        determinism
                        ▼  + sha256             commit nothing)     guard
                  EVIDENCE REPOS ──────────────────────────────────────────►
                  append-only, never rewritten            │
                        │                                 ▼
                        │  deterministic extraction   DERIVED REPO (per article)
                        ▼  (immutable profiles)           │
                  LEX-INDEX/3 + LOCAL VECTORS ◄───────────────┘
                        │  signed whole-artifact manifest
                        │  verified at build and startup
                        ▼
                  CANDIDATE REVISION (zero traffic)
                        │  health + MCP + LU/EU search smoke tests
                        ▼
                  MCP server · this site (Container Apps, one pinned replica)</pre></div>
                <p class="sub">Azure: Container Apps behind a managed certificate, Container Registry, Key Vault
                signing, Application Insights via OpenTelemetry, and Azure DNS. Managed identity pulls the image
                and authenticates the optional Azure OpenAI assistant. Retrieval itself is local and deterministic:
                FTS5/BM25, ONNX embeddings, fixed reranking and fusion, with no generative model in the search path.
                The web app uses 1 vCPU and 2 GiB for the mounted lexical and semantic artifacts,
                and runs as <b>one always-on replica</b>. The single process makes the public
                request and concurrency ledgers authoritative while keeping retrieval latency predictable.</p>

                <h2>Decisions worth defending</h2>
                <div class="card"><table>
                <tr><th>decision</th><th>why, and what it cost</th></tr>
                <tr><td><b>Store publisher bytes verbatim</b></td>
                    <td>The evidence layer is never "cleaned". A hash over cleaned text proves nothing about
                    what the state published. Cost: two layers to maintain instead of one.</td></tr>
                <tr><td><b>Extraction profiles are immutable</b></td>
                    <td>Once <span class="mono">akn-lu/1</span> is published, its output for a given input can
                    never change, a frozen-fingerprint test fails the build if it does. Improvements ship as
                    a new profile. Cost: no silent fixes, ever.</td></tr>
                <tr><td><b>Refusals are part of the API</b></td>
                    <td>Seven typed refusal codes instead of empty results. A caller can distinguish "no such
                    law", "no version that day" and "text withheld". Cost: more surface to test.</td></tr>
                <tr><td><b>Bounded retrieval, deterministic authority</b></td>
                    <td>Application code resolves named works and authorizes work-specific tools. A bounded
                    tool-calling loop gathers MCP evidence; Agent Framework then composes claim-typed prose and
                    runs the conditional judge. Citations and typed gaps remain deterministic application contracts.</td></tr>
                <tr><td><b>Nightly commits nothing when unsure</b></td>
                    <td>A &gt;5% drop in works, or a re-derivation that is not byte-identical, aborts the run.
                    A partial upstream response must never rewrite history.</td></tr>
                </table>
                <p class="sub" style="margin:8px 0 0">{architecture.Decisions.Count:n0} delivery decisions like these are read from
                the architecture registry, alongside the numbered specification decisions. Each records its
                rationale, so "why did you do it that way" has a written answer rather than a recollection.</p></div>

                <h2>The machinery that keeps it fresh</h2>
                <p>Law changes while you sleep, so the corpus is rebuilt while I do. One scheduled job at
                02:17 UTC drives the whole fleet, no manual publication step exists. GitHub Actions uses OIDC
                and short-lived Azure authorization; the signing key remains non-exportable in Key Vault.</p>
                <div class="card"><table>
                <tr><th>stage</th><th>what it does, and how it refuses to do damage</th></tr>
                <tr><td><b>1. Ingest</b></td><td>Asks each publisher what versions exist, downloads any it has
                not seen, and writes them <i>verbatim</i>. Existing files are never reopened for writing,
                the evidence layer is append-only by construction.</td></tr>
                <tr><td><b>2. Anomaly gate</b></td><td>If the work count drops more than 5%, the run assumes the
                upstream response was partial, discards everything and commits nothing. A bad night leaves
                yesterday's good data in place.</td></tr>
                <tr><td><b>3. Derive</b></td><td>Regenerates the per-article layer from the verbatim files.</td></tr>
                <tr><td><b>4. Determinism guard</b></td><td>If derived output changed while no source file did,
                that means the extractor is non-deterministic, the run fails loudly and commits nothing,
                because a silent extraction drift would corrupt history.</td></tr>
                <tr><td><b>5. Index &amp; sign</b></td><td>Builds lex-index/3, local vectors and the benchmark evidence.
                A canonical manifest binds every index, vector, model, tokenizer and scope artifact by hash and
                size; Key Vault signs that whole manifest and the workflow verifies it before publication.</td></tr>
                <tr><td><b>6. Deploy safely</b></td><td>Builds an immutable image tagged with the code commit and
                manifest hash, verifies the release again, starts a zero-traffic revision, exercises health, MCP,
                LU and EU search, then promotes it. The preceding revision remains available for rollback.</td></tr>
                <tr><td><b>7. Report</b></td><td>Writes a three-state outcome per publisher
                (<span class="mono">ran_committed</span> / <span class="mono">ran_no_change</span> /
                <span class="mono">failed_*</span>) and opens an issue on failure.</td></tr>
                </table></div>
                <p>The result travels with the data. The pinned whole-artifact manifest is the runtime trust root;
                each index also carries a public provenance stamp recording when and from which corpus commit it
                was built. Every tool response returns that provenance. <b>This is the embedded stamp read live
                from the running indexes:</b></p>
                <div class="card"><table tabindex="0" aria-label="Mounted index provenance">
                <tr><th>publisher</th><th>index built</th><th>from corpus commit</th><th>signature</th></tr>
                {string.Join("", readers.Values.OrderBy(r => r.Collection, StringComparer.Ordinal).Select(r => $"""
                    <tr><td>{H(r.Collection)}</td>
                    <td class="mono">{H(r.Stamp.GetValueOrDefault("built_at"))}</td>
                    <td class="mono">{H(r.Stamp.GetValueOrDefault("corpus_commit"))}</td>
                    <td>{(r.SignatureValid ? "<span class=\"badge ok\">valid</span>" : "<span class=\"badge warn\">unsigned</span>")}</td></tr>
                    """))}
                </table>
                <p class="sub" style="margin:8px 0 0">Nothing here is typed by hand. The same values come back
                from the <a href="/developers">coverage tool</a>. The embedded signature proves provenance; the
                <a href="/verify">release verification page</a> reports whether the complete mounted artifact set
                passed the independently pinned trust policy.</p></div>

                <h2>What broke, and what it taught</h2>
                <div class="card">
                <p><b>A silently dead search, caught in production.</b> The nightly job built the search index
                without the per-article layer. Nothing errored: the index was valid, signed, and published,
                it just had zero provisions in it, so search returned nothing. The automated eval suite caught
                it by asking a question a user would ask and noticing the assistant could no longer find a
                Luxembourg code.</p>
                <p class="sub">Fix: the index step now runs after derivation and takes the article layer as a
                required input, so the failure cannot recur. Lesson: a green build is not a working system,
                the only tests that would have caught this are the ones that exercise it end to end, the way
                someone actually uses it.</p>
                <p><b>A parser that quietly duplicated text.</b> Adding Formex XML support introduced doubled
                paragraphs where an article had introductory text followed by a list. It looked plausible on
                screen. It was caught by re-reading real output rather than trusting a passing test, fixed the
                same day, and pinned with a fingerprint test so the profile can never drift again.</p>
                </div>

                <h2>How correctness is proven, not claimed</h2>
                <div class="card"><table tabindex="0" aria-label="Correctness evaluation layers">
                <tr><th>mechanism</th><th>what it guarantees</th></tr>
                <tr><td>Unit, contract, golden and architecture-fitness suites</td><td>parsers, temporal logic,
                backward-compatible APIs, pages, index schemas, trust and deployment invariants</td></tr>
                <tr><td>Frozen profile fingerprints</td><td>a published extraction can never change output</td></tr>
                <tr><td>Determinism guard in CI</td><td>re-derivation is byte-identical or the run commits nothing</td></tr>
                <tr><td>End-to-end assistant evals</td><td>the assistant picks the right tools and never cites a source it was not given</td></tr>
                <tr><td>200-case retrieval benchmark</td><td>EU and Luxembourg exact, temporal, conceptual,
                bilingual, typo, hierarchy, role, comparison, negative, ambiguity and gap behavior is
                measured on separate tuning and holdout judgments before hybrid can become the default</td></tr>
                <tr><td>LLM-judged groundedness</td><td>answers scored against the evidence actually returned</td></tr>
                <tr><td>Pinned whole-artifact manifests</td><td>anyone can verify every released input was not altered,
                <a href="/verify">recipe</a></td></tr>
                </table></div>

                <h2>Scale</h2>
                <p><span class="badge">{cov.Sum(c => c.Groups):n0} works</span>
                <span class="badge">{cov.Sum(c => c.Versions):n0} dated versions</span>
                <span class="badge">lex-index/2 + lex-index/3 readers</span>
                <span class="badge">3 official XML/HTML dialects</span>
                <span class="badge">nightly, unattended</span></p>

                <h2>What I would do differently</h2>
                <p>Version the index schema migration path from day one, schema v2 required a full rebuild
                rather than a migration. Put the end-to-end evals in the nightly pipeline, not only in my hands;
                they caught the worst bug of the project and should be a gate, not a habit. And treat the
                derived layer's release assets as part of the deploy, not a follow-up step, the two times
                something shipped stale, that was why.</p>

                <!-- Eight sections on how this was made, and until now not one word on who made it.
                     Anyone still reading here has already decided the work is serious, so the answer
                     belongs at the end rather than at the top: a law tool that opens by introducing its
                     author reads as a portfolio, and stops being trusted as a source. Two sentences. -->
                <h2>Who built this</h2>
                <div class="card">
                <p>Lex is built and run by <b><a href="/about">Soufien Hajji</a></b>, a Lead Software &amp; AI
                Engineer specialising in taking enterprise AI copilots from prototype to production on Azure,
                based near Luxembourg. It is a personal project, unaffiliated with any publisher or
                public body, built the way I build professionally: the pipeline is deterministic, the claims
                are testable, and the parts that cannot be verified say so rather than guessing.</p>
                <p class="sub"><a href="/about"><b>About, and the other two systems built the same way →</b></a></p>
                </div>

                <p class="sub"><a href="/decisions"><b>Why it is built this way →</b></a> ·
                <a href="/developers"><b>Use it from your own code →</b></a> ·
                <a href="/architecture"><b>The data model →</b></a> ·
                <a href="/verify"><b>Verify a build yourself →</b></a> ·
                <a href="https://github.com/SFHAJJI/lex-articles" rel="noopener"><b>The dataset →</b></a></p>
                """;
            return Results.Content(Page("How it was built", body, null, "how",
                canonicalPath: "/built"), "text/html");
        });

        // ---- /about: who built this, and what else they have built.
        //
        // Deliberately one page, and deliberately not the front door. Someone who ends up holding this
        // app, or who reads it far enough to wonder who is behind it, should find a straight answer in
        // one click rather than piecing it together from a footer. Everywhere the author or the sibling
        // projects were named inline now points here, so the answer lives in exactly one place.
        app.MapGet("/about", () =>
        {
            var cov = ctx.Registry.Values.Select(r => r.Coverage()).ToList();
            var works = cov.Sum(c => c.Groups);
            var versions = cov.Sum(c => c.Versions);
            var body = $$"""
                <p class="lede">I build systems that have to be right rather than plausible: regulated
                platforms where an answer has to carry its source, its date, and a way to check it.</p>

                <div class="card">
                <p><b>Soufien Hajji</b>, Lead Software &amp; AI Engineer specialising in taking enterprise AI
                copilots from prototype to production on Azure, based near Luxembourg. Nine years across
                telecoms, rail, investment banking and asset management, most of it on
                systems where being wrong is expensive: front-office trading platforms, regulatory-capital
                reporting, and the agentic AI layer on top of it.</p>
                <p class="sub">Lex is a personal project, unaffiliated with any publisher or public body.
                It is built the way I build professionally, which is the reason it exists: the pipeline is
                deterministic, the claims are testable, and the parts that cannot be verified say so
                instead of guessing.</p>
                <p><a class="pick main" href="https://api.soufien.lu/cv/en/download" rel="noopener">CV, English (PDF)</a>
                &nbsp; <a class="pick" href="https://api.soufien.lu/cv/fr/download" rel="noopener">CV, français (PDF)</a>
                &nbsp; <a class="pick" href="https://www.linkedin.com/in/hajji-soufien" rel="noopener">LinkedIn ↗</a></p>
                </div>

                <h2>Three things, built the same way</h2>

                <div class="card">
                <b><a href="https://law.soufien.lu">law.soufien.lu</a></b> &middot; this one
                <p class="sub">Point-in-time Luxembourg and reviewed-scope EU law: {{works:n0}} works as
                {{versions:n0}} dated versions, a public MCP server, open datasets, and a signed index
                whose stamp commits to a digest of its own content. The hard part was never the AI; it was
                that a law has no single text, only a text per date.
                <a href="/decisions">The decision that shaped it, and what it cost →</a></p>
                </div>

                <div class="card">
                <b><a href="https://soufien.lu" rel="noopener">soufien.lu</a></b> &middot; the assistant
                <p class="sub">Ask about my experience or my code, drop in a job advert to see how it
                matches, or run a mock interview. Grounded in a corpus of my own work, and it cites where
                each answer came from.</p>
                </div>

                <div class="card">
                <b><a href="https://energy.soufien.lu" rel="noopener">energy.soufien.lu</a></b> &middot; solar prospecting
                <p class="sub">An AI co-pilot for photovoltaic prospecting in Luxembourg: which roofs and
                car parks qualify, the tariffs and grants that apply, and the law behind the answer. Every
                claim is cited, and it declines rather than guessing.</p>
                </div>

                <h2>How I work, if that is what you are here for</h2>
                <p>The clearest evidence is not a CV. It is
                <a href="/decisions">the decisions page</a>, which states each choice, the alternative it
                was taken over, and the bill; <a href="/coverage">the coverage page</a>, which exists to
                say what this service does <i>not</i> hold; and
                <a href="/verify">the verification page</a>, which tells you how to check the answers
                without trusting me. A system that cannot say what it lacks is not finished.</p>

                <p class="sub"><a href="/built"><b>How this was built →</b></a> &middot;
                <a href="/decisions"><b>Why it is built this way →</b></a> &middot;
                <a href="https://github.com/SFHAJJI" rel="noopener"><b>GitHub →</b></a></p>
                """;
            return Results.Content(Page("About", body,
                "Lead Software and AI Engineer taking enterprise AI copilots from prototype to production on Azure.",
                "about", canonicalPath: "/about"), "text/html");
        });

        // ---- /decisions: the choices, with what each one cost.
        //
        // /built says how the system works. This says why it is that way and what the alternative would
        // have been, which is the part a reader can actually argue with. One entry per decision that had
        // a defensible other answer; a page of choices with no cost attached is marketing.
        app.MapGet("/decisions", () =>
        {
            var cov = readers.Values.Select(r => r.Coverage()).ToList();
            var latest = cov.Select(c => c.LatestValidFrom).Where(x => x is not null).Max();
            var programRows = string.Join("", architecture.Decisions.Select(d => $"""
                <tr><td class="mono">{H(d.Id)}</td><td><b>{H(d.Title)}</b><br>
                <span class="sub"><b>Choice:</b> {H(d.Choice)}<br><b>Alternative:</b> {H(d.Alternative)}<br>
                <b>Why:</b> {H(d.Reason)}<br><b>Cost:</b> {H(d.Cost)}</span></td><td>{StatusBadge(d.Status)}</td></tr>
                """));
            var body = ArchitectureTabs("decisions") + $"""
                <p class="lede">Every program decision records the chosen path, a credible alternative, the
                reason and the bill. Status comes from the architecture registry.</p>
                <div class="card"><table tabindex="0" aria-label="Architecture decision register"><tr><th>decision</th><th>choice, alternative and cost</th><th>status</th></tr>
                {programRows}</table></div>
                <h2>Deep dive: why the legislative timeline is not the git log</h2>
                """ + $$"""
                <p class="lede">Every entry here had a reasonable alternative that other people chose. What
                follows is the choice, the road not taken, and the bill.</p>

                <h2>The history is not the git log</h2>

                <div class="card">
                <p><b>The choice.</b> The corpus is append-only git. The history is not.</p>
                <p>Every consolidated file a publisher issues is stored verbatim, under its sha256, in a
                repository that only ever gains commits. That is the evidence, and anyone can audit it with
                <span class="mono">git clone</span>. But no query walks it. Point-in-time answers come from
                a signed SQLite index carrying three separate time axes, rebuilt from the corpus every
                night. The only <span class="mono">git</span> call anywhere in the engine is
                <span class="mono">rev-parse HEAD</span>, which stamps the index with the exact commit it
                was built from.</p>
                </div>

                <h3>The alternative</h3>
                <p>Store each law as a file and let git be the history: a commit per version,
                <span class="mono">git log</span> for the timeline, <span class="mono">git diff</span>
                between two dates. It is elegant, it is nearly free, and it arrives with a browsable web
                interface that somebody else operates. Independent projects run on exactly this.
                <a href="https://github.com/Legilibre/Archeo-Lex" rel="noopener nofollow">Archeo-Lex</a>,
                by Legilibre, replays French law from the LEGI database as Git and Markdown, one commit
                per consolidated version. <a href="https://github.com/bundestag/gesetze" rel="noopener nofollow">bundestag/gesetze</a>
                does the same for German federal law from gesetze-im-internet.de; it is a community
                project rather than the parliament, despite the organisation name. It was the obvious
                thing to do, and I did not do it.</p>

                <p>The German project is worth reading on its own commits: they aim to follow
                publication in the <i>Bundesgesetzblatt</i>, and, in its words,
                &#8220;das funktioniert nicht immer problemlos&#8221;, this does not always work
                smoothly. That is the same wall met from the other side. Nothing is wrong with their
                engineering; a commit graph is simply not shaped like a legislative timeline.</p>

                <h3>Why not</h3>
                <p>Five reasons. Each is a fact about legislation rather than a preference about tools.</p>
                <div class="card">
                <p><b>1. Git's clock is the wrong clock.</b> A commit records when <i>I</i> learned
                something, never the publisher's legal-time coordinate. Legilux supplies applicability
                dates; EUR-Lex supplies dates for official consolidated wording states. Git timestamps
                both with the observation time. <span class="mono">git log</span> answers "when did we find
                out", which is a real question, but not either publisher timeline.</p>

                <p><b>2. Luxembourg law is dated into the future.</b> The mounted corpus holds Legilux applicability dates to
                <span class="mono">{{H(latest)}}</span>. Publishers routinely issue today a text that
                becomes binding years from now. Git cannot express a commit that becomes true later, so a
                git-as-history model must either publish future law as if it were current, or drop it.
                Both are wrong answers to "what is in force today".</p>

                <p><b>3. Publishers backfill.</b> A consolidation covering 2019 can be issued in 2026.
                Under git-as-history that arrives as a 2026 change to the law, which is simply false. The
                fix is to record two things separately: the publisher's stated wording coordinate, with
                its declared semantics, and when we observed it. That is structured bitemporal evidence,
                and it is not something a commit graph can carry.</p>

                <p><b>4. <span class="mono">git diff</span> cannot see a renumbering.</b> When Article 7
                becomes Article 7-1 with its wording untouched, that is a rename <i>inside</i> a file, not
                a file rename, and a textual diff reports one deletion and one insertion. Lex detects it
                mechanically instead: a renumbering event is emitted only when the text hash matches across
                the change of anchor. None of that is inferable from a diff.</p>

                <p><b>5. There is no per-article axis.</b> "When did Article L. 111-1 last change" needs
                either one file per article, which no publisher provides, or a table keyed on
                (work, anchor, valid_from). Lex has the table. A repository of whole documents cannot
                answer the question at all.</p>
                </div>

                <h3>What it cost</h3>
                <p>Four things. An argument that omits them is not worth reading.</p>
                <div class="card">
                <p><b>A build step, every night.</b> Git-as-history is free: commit, and you are finished.
                Lex has to ingest, derive, catalog, index and sign before anything is answerable, and that
                pipeline is the largest part of the codebase.</p>
                <p><b>Duplication.</b> The same facts now exist twice, as bytes in the corpus and as rows
                in an index, and two copies can disagree. That is exactly why the index stamp binds a
                digest of its own content and names the corpus commit it came from: the duplication is
                allowed, drifting silently is not.</p>
                <p><b>Schema drift.</b> A column was once added to the index without changing the schema
                string, so an index built the day before opened cleanly and then failed inside a request
                with a raw SQL error. A repository of files has no schema to drift. Opening an index now
                checks that every column the reader needs is present, and refuses with a message naming
                what is missing.</p>
                <p><b>The free interface.</b> GitHub hands a git-as-history project a browsable, diffable,
                permalinked view that somebody else maintains. Choosing a data layer meant building all of
                it, and the reading, comparing and searching on this site is the bill for that decision.</p>
                </div>

                <h3>What it bought</h3>
                <p>A point-in-time answer is one indexed lookup rather than a walk backwards through
                history. Future-dated law is representable. Every record separates what the publisher
                asserts from what Lex observed, so "what did it say" and "what did we know" stay different
                questions. Articles have their own lifetimes, renumbering included. And because an answer
                comes from a single artifact rather than a traversal, that artifact can be signed: the
                stamp commits to a digest of the content, so an index that was altered fails verification
                instead of serving quietly.</p>

                <h3>Check it yourself</h3>
                <p class="sub">
                <a href="/verify"><b>Verify a build &rarr;</b></a> &middot;
                <a href="/architecture"><b>The data model &rarr;</b></a> &middot;
                <a href="https://github.com/SFHAJJI/lex-corpus-lu-legilux" rel="noopener"><b>The evidence repo &rarr;</b></a> &middot;
                <a href="/coverage"><b>What is missing &rarr;</b></a></p>

                <p class="sub" style="margin-top:26px">More entries as they are written. If you disagree
                with one of these, that is the point of publishing them:
                <a href="https://github.com/SFHAJJI/lex/issues" rel="noopener">open an issue</a>.</p>
                """;
            return Results.Content(Page("Decisions", body,
                "The choices that shaped Lex, each with the alternative it was chosen over and what it cost.",
                "how", canonicalPath: "/decisions"), "text/html");
        });

        // ---- /developers: everything an engineer needs — every tool, four ways to connect,
        // the datasets, the repos. /ai kept as an alias so older links survive.
        app.MapGet("/developers", (HttpRequest req) =>
        {
            var baseUrl = BaseUrl(req);
            var cov = readers.Values.Select(r => r.Coverage()).ToList();
            // Counted from the advertised tool list, never written by hand. This page said "Eight" in its
            // lede and "nine" in its heading while the endpoint served ten, because three places had to be
            // remembered every time a tool shipped.
            var tools = mcpCore.ToolDefs().OfType<JsonObject>()
                               .Select(t => t["name"]!.GetValue<string>()).ToList();
            var body = $$"""
                <p class="lede" id="assistant">Lex is MCP-native: you bring the model, Lex brings the evidence.
                {{tools.Count}} read-only tools over signed indexes, with no key or account required.
                The public endpoint is deliberately bounded and advertises MCP {{McpSdkBridge.ServerVersion}}.</p>

                <h2 id="assistant-data">Assistant data and public limits</h2>
                <div class="card">
                <p>The browser retains at most six conversation turns in this tab's
                <span class="mono">sessionStorage</span>. A submitted bounded transcript is sent to this
                server and to Azure OpenAI when planning or optional synthesis is required. Starting a new
                conversation clears that transcript but leaves the legal workspace in place. Do not submit
                confidential client facts.</p>
                <p>The application is server-stateless for conversation content. It keeps short-lived,
                in-memory request identities for ten minutes. Completed responses are replayed while held
                inside a 64 MiB cache; an evicted identity remains a tombstone and cannot execute again. Daily
                assistant counters and rolling MCP counters use an ingress-derived client address in process
                memory; raw addresses and raw user text are not written to application logs, traces, metrics
                or error bodies. URL queries and address attributes are redacted before export. OpenTelemetry
                records an allowlist of model deployment, opaque operation ID, tool, status and document count.
                The deployed Application Insights request, dependency and trace tables retain that bounded
                telemetry for 90 days; deployment fails if those table policies differ. This deployment uses
                Azure OpenAI's standard abuse-monitoring posture, under which Microsoft may retain prompts and
                completions for up to 30 days. They are not used to train foundation models.</p>
                <p>Public MCP admits at most 8 executing and 16 queued calls, with a 2 second queue deadline;
                hybrid search admits 2 at once. Rolling limits are 120 calls per trusted client and 600 calls
                globally per minute. These are best-effort abuse controls: people behind one NAT can share an
                address, and IPv6 addresses can rotate. Requests, strings, pagination and returned collections
                are bounded before tool execution.</p>
                </div>

                <h2>Connect</h2>
                <div class="card"><b>Claude Code</b>
                <pre class="mono" style="white-space:pre-wrap;margin:6px 0 0">claude mcp add --transport http lex {{baseUrl}}/mcp</pre></div>
                <div class="card"><b>VS Code, Cursor, or another client with remote HTTP support</b>
                <pre class="mono" style="white-space:pre-wrap;margin:6px 0 0">{ "servers": { "lex": { "type": "http", "url": "{{baseUrl}}/mcp" } } }</pre></div>
                <div class="card"><b>Legacy stdio-only client</b>, use the pinned third-party bridge
                (Node.js 18+; Lex remains hosted):
                <pre class="mono" style="white-space:pre-wrap;margin:6px 0 0">{ "mcpServers": { "lex": { "command": "npx", "args": ["-y", "mcp-remote@0.1.38", "{{baseUrl}}/mcp"] } } }</pre></div>
                <div class="card"><b>Azure AI Foundry Agent Service</b>, remote MCP is native:
                <pre class="mono" style="white-space:pre-wrap;margin:6px 0 0">{ "type": "mcp", "server_label": "lex", "server_url": "{{baseUrl}}/mcp", "require_approval": "never" }</pre></div>
                <div class="card"><b>No framework at all</b>, it is JSON-RPC over one POST:
                <pre class="mono" style="white-space:pre-wrap;margin:6px 0 0">curl -X POST {{baseUrl}}/mcp -H 'Content-Type: application/json' -H 'Accept: application/json, text/event-stream' \
                  -d '{ "jsonrpc":"2.0", "id":1, "method":"tools/call",
                        "params": { "name":"as_of",
                          "arguments": { "work":"eu-eurlex:32016r0679",
                            "date":"2019-03-15", "mode":"select", "anchors":"art_33" } } }'</pre></div>

                <h2>The {{tools.Count}} tools</h2>
                <div class="card"><table>
                <tr><th>tool</th><th>arguments</th><th>answers</th></tr>
                <tr><td class="mono">as_of</td><td class="mono">work, date, [language], [mode: full\|outline\|select], [anchors]</td>
                    <td>the publisher wording state covering a date. For Legilux this is applicability; for EUR-Lex it is an official consolidated wording state. <span class="mono">outline</span> lists article anchors only;
                    <span class="mono">select</span> returns just the anchors you name, use it, codes are large.</td></tr>
                <tr><td class="mono">article_history</td><td class="mono">work, anchor</td>
                    <td>every distinct text one article has had on its publisher timeline, plus renumbering events.</td></tr>
                <tr><td class="mono">timeline</td><td class="mono">work</td><td>all publisher versions of a work with explicit timeline semantics.</td></tr>
                <tr><td class="mono">diff</td><td class="mono">work, from_date, to_date, [language]</td><td>what changed between two versions.</td></tr>
                <tr><td class="mono">in_force_on</td><td class="mono">date, [publisher|jurisdiction], [source_class|document_type], [hierarchy], [act_form], [binding_status], [domain], [language], [limit], [offset]</td>
                    <td>publisher states covering a day: Legilux applicability and EUR-Lex official consolidation states, distinguished in the envelope.</td></tr>
                <tr><td class="mono">search</td><td class="mono">query, [publisher|jurisdiction], [retrieval_mode], [time_scope], [as_of], [fuzzy], [source_class|document_type], [hierarchy], [act_form], [binding_status], [domain], [language], [works], [limit]</td>
                    <td>provision-level search across Luxembourg and EU law. Keyword is deterministic
                    FTS5/BM25. Hybrid adds the pinned local encoder only when verified vectors are mounted.
                    Hits identify the exact applicable version and link back to the authoritative article.</td></tr>
                <tr><td class="mono">provenance</td><td class="mono">lex_id</td>
                    <td>source URI, retrieval time, record hash, the append-only observation chain.</td></tr>
                <tr><td class="mono">coverage</td><td class="mono">[publisher]</td><td>what is held, and what is knowably missing.</td></tr>
                <tr><td class="mono">cited_by</td><td class="mono">work, [limit]</td>
                    <td>which articles point AT this law, from the cross-references the publisher writes
                    into its own text. Answers "what depends on this", "who amended it".</td></tr>
                <tr><td class="mono">changes_in_period</td><td class="mono">from_date, to_date, [publisher|jurisdiction], [source_class|document_type], [hierarchy], [act_form], [binding_status], [domain], [language], [order], [limit], [offset]</td>
                    <td>across the corpus: which works gained versions in a window, and how many.
                    The aggregate counterpart of diff and timeline, which cover one work.</td></tr>
                </table></div>

                <h2>Try the joysticks</h2>
                <p class="sub">This calls the same public endpoint your model would. No key, nothing installed , 
                the JSON below is exactly what an MCP client receives.</p>
                <div class="card">
                  <form id="pg" class="inline" style="margin:0 0 8px">
                    <select id="pgtool" aria-label="Choose a tool to call">
                      {{string.Join("", tools.Select(t => $"<option value=\"{t}\">{t}</option>"))}}
                    </select>
                    <button type="submit">Call it</button>
                  </form>
                  <textarea id="pgargs" aria-label="Tool arguments as JSON" rows="5" style="width:100%;font-family:var(--mono);font-size:13px"></textarea>
                  <pre id="pgout" class="mono" style="white-space:pre-wrap;max-height:340px;overflow:auto;font-size:12.5px;margin:10px 0 0">↑ pick a tool and press "Call it"</pre>
                </div>

                <h2>It refuses rather than guesses</h2>
                <p class="sub">Every tool returns an envelope with a status. The refusals are part of the contract:
                <span class="mono">no_version_for_date</span> · <span class="mono">unknown_work</span> ·
                <span class="mono">unknown_anchor</span> · <span class="mono">anchor_not_in_version</span> ·
                <span class="mono">no_provision_history</span> · <span class="mono">text_withheld</span> ·
                <span class="mono">text_not_available</span>. Build against them: an empty result and a
                refusal are different things. See the
                <a href="https://github.com/SFHAJJI/lex/blob/main/docs/mcp-2-migration.md" rel="noopener">MCP 2.0 migration note</a>.</p>

                <h2>Or skip the API, take the data</h2>
                <p>Every provision of every version, one row each, licence and attribution inline.
                {{cov.Sum(c => c.VersionsWithText):n0}} versions carry full text.</p>
                <div class="card"><b>DuckDB, one line, no download:</b>
                <pre class="mono" style="white-space:pre-wrap;margin:6px 0 0">SELECT * FROM read_parquet('https://github.com/SFHAJJI/lex-articles/releases/latest/download/eu-eurlex-provisions.parquet');</pre>
                <p class="sub" style="margin:8px 0 0">Also published as <span class="mono">.jsonl.gz</span>.
                Python examples (standard library only, no pip install) live in
                <a href="https://github.com/SFHAJJI/lex-articles/tree/main/examples" rel="noopener">examples/</a> , 
                load provisions, resolve a point in time, verify the hash chain, call this MCP endpoint.</p></div>

                <h2>The repositories</h2>
                <div class="card"><table>
                <tr><th>repo</th><th>what it is</th><th>licence</th></tr>
                <tr><td><a href="https://github.com/SFHAJJI/lex" rel="noopener">lex</a></td>
                    <td>the engine: ingest, derive, index, MCP server, this site</td><td>Apache-2.0</td></tr>
                <tr><td><a href="https://github.com/SFHAJJI/lex-articles" rel="noopener">lex-articles</a></td>
                    <td>derived layer, one Markdown+JSON record per article per version</td><td>CC-BY-4.0</td></tr>
                <tr><td><a href="https://github.com/SFHAJJI/lex-corpus-lu-legilux" rel="noopener">lex-corpus-lu-legilux</a></td>
                    <td>evidence layer, Legilux's own files, verbatim, plus signed nightly index</td><td>CC-BY-4.0</td></tr>
                <tr><td><a href="https://github.com/SFHAJJI/lex-corpus-eu-eurlex" rel="noopener">lex-corpus-eu-eurlex</a></td>
                    <td>evidence layer, EUR-Lex/Cellar files, verbatim</td><td>EU reuse</td></tr>
                </table>
                <p class="sub" style="margin:8px 0 0">Why four? Evidence and derivation are kept apart on purpose:
                the evidence repos are append-only and never rewritten, so a derived record can always be traced
                back to the exact bytes a state published. <a href="/architecture">The full model →</a></p></div>

                <p class="sub">Everything on this page is exercised by the project's own test and eval suites,
                and the endpoint is rebuilt nightly. <a href="/built">How it was built →</a> ·
                <a href="/verify">Verify it yourself →</a></p>
                """
                + """
                <script>
                (function () {
                  const presets = {
                    as_of: { work: "eu-eurlex:32016r0679", date: "2019-03-15", mode: "select", anchors: "art_33" },
                    article_history: { work: "eu-eurlex:32013r0575", anchor: "art_92" },
                    timeline: { work: "lu-legilux:loi-2020-07-17-a624" },
                    diff: { work: "lu-legilux:loi-2020-07-17-a624", from_date: "2020-07-25", to_date: "2021-02-01" },
                    in_force_on: { date: "2022-03-15", document_type: "CODE", limit: 5 },
                    search: { query: "congé parental", jurisdiction: "lu", retrieval_mode: "keyword",
                              time_scope: "as_of", as_of: "2022-03-15", fuzzy: "auto", limit: 3 },
                    provenance: { lex_id: "eu-eurlex:32016r0679:2016-05-04" },
                    coverage: {},
                    changes_in_period: { from_date: "2020-03-01", to_date: "2021-07-01", order: "by_churn", limit: 10 }
                  };
                  const tool = document.getElementById('pgtool'), args = document.getElementById('pgargs'),
                        out = document.getElementById('pgout'), form = document.getElementById('pg');
                  function fill() { args.value = JSON.stringify(presets[tool.value], null, 2); }
                  tool.addEventListener('change', fill); fill();
                  form.addEventListener('submit', async function (e) {
                    e.preventDefault();
                    out.textContent = 'calling ' + tool.value + '…';
                    let parsed;
                    try { parsed = JSON.parse(args.value || '{}'); }
                    catch (err) { out.textContent = 'arguments are not valid JSON: ' + err.message; return; }
                    try {
                      const r = await fetch('/mcp', {
                        method: 'POST', headers: { 'Content-Type': 'application/json', 'Accept': 'application/json, text/event-stream' },
                        body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'tools/call',
                                               params: { name: tool.value, arguments: parsed } })
                      });
                      const raw = await r.text();
                      const data = r.headers.get('content-type')?.startsWith('text/event-stream')
                        ? raw.split('\n').filter(line => line.startsWith('data: ')).at(-1)?.slice(6)
                        : raw;
                      const j = JSON.parse(data || '{}');
                      const text = j.result && j.result.content && j.result.content[0]
                        ? j.result.content[0].text : JSON.stringify(j, null, 2);
                      let pretty; try { pretty = JSON.stringify(JSON.parse(text), null, 2); } catch { pretty = text; }
                      out.textContent = pretty.length > 6000 ? pretty.slice(0, 6000) + '\n… truncated for display' : pretty;
                    } catch (err) { out.textContent = 'request failed: ' + err.message; }
                  });
                })();
                </script>
                """;
            return Results.Content(Page("For developers", body, null, "dev",
                canonicalPath: "/developers"), "text/html");
        });

        // ---- /how-it-works: one page, plain language first, technical depth on scroll.
        // Absorbs what used to be three separate tabs (architecture, verify, coverage).
        app.MapGet("/how-it-works", () =>
        {
            var cov = readers.Values.Select(r => r.Coverage()).ToList();
            var body = $"""
                <p class="lede">Short version: Lex keeps every dated version of a law exactly as the official
                publisher issued it, proves it has not altered a byte, and refuses to answer when it does not
                know. Here is the longer version.</p>

                <h2>Why "what does the law say" is the wrong question</h2>
                <p>Laws are amended constantly. Ask what the Luxembourg Covid measures act said and the honest
                answer is: <i>which of its 32 texts?</i> Almost every legal question that matters, was this
                contract valid, was that fine lawful, did we comply, is really a question about a
                <b>date</b>. Most legal websites show you only today's text.</p>

                <h2>Where the text comes from</h2>
                <p>Only from the official sources: <a href="https://legilux.public.lu" rel="noopener">Legilux</a>
                for Luxembourg, <a href="https://eur-lex.europa.eu" rel="noopener">EUR-Lex</a> for the EU.
                Lex downloads the publisher's own file for each version and stores it <b>byte for byte</b>,
                untouched, with a SHA-256 fingerprint. Nothing is rewritten, summarised or "cleaned".</p>
                <p>A second layer then splits that file into one record per article, still deterministic,
                no AI involved, so you can ask for Article 92 rather than a whole 600-page regulation.
                Each article's fingerprint chains back to the publisher's original file, so any tampering
                anywhere in the chain is detectable.</p>

                <h2>How you can check all of this yourself</h2>
                <p>Every index Lex serves is cryptographically signed (ECDSA P-256). You can download the
                public key, verify the signature, recompute any article's hash, and compare it against the
                publisher's own file, all without trusting this site.
                <a href="/verify"><b>The step-by-step recipe, with code →</b></a></p>

                <h2>What it will not do</h2>
                <p>It will not guess. If you ask for a date Lex has no version for, it says so with a reason
                code (<span class="mono">no_version_for_date</span>) instead of producing a plausible text.
                The assistant on the front page may plan searches and explain retrieved evidence, but it cannot
                authorize its own law choice or invent legal text. Deterministic guards resolve names, require
                clarification when evidence is weak, validate citations and preserve publisher gaps. Every
                factual answer shows the evidence underneath it so you can check the source without leaving the page.
                <a href="/lu-legilux/rgd-1998-08-03-n4/1900-01-01">Watch it refuse →</a></p>

                <h2>What it holds today</h2>
                <p><span class="badge">{cov.Sum(c => c.Groups):n0} works</span>
                <span class="badge">{cov.Sum(c => c.Versions):n0} dated versions</span>
                <span class="badge">{cov.Sum(c => c.VersionsWithText):n0} with full text</span>
                <span class="badge">refreshed nightly</span></p>
                <p>Lex holds every current record in Legilux's consolidation catalogue, plus the selected
                temporal EU regulatory scope shown by the mounted index. Legilux also exposes a much broader
                original-act catalogue; it mixes lawyer-relevant rules with notices and document classes that
                need separate date and text semantics, so it is not presented as already covered. The gaps are
                published rather than hidden.
                <a href="/coverage"><b>Exactly what is and is not held →</b></a></p>

                <h2>Under the hood</h2>
                <p>The two-layer data model, the extraction profiles, the index schema and the refusal
                taxonomy are documented for engineers.
                <a href="/architecture"><b>The architecture page →</b></a> ·
                <a href="/built"><b>How it was built, and what was hard →</b></a> ·
                <a href="/developers"><b>Use it from your own code →</b></a></p>

                <div class="notice"><b>Not legal advice, and not the official text.</b> Consolidated versions
                have no legal force, only the version published in the official gazette does. The publishers
                say so themselves. Lex reports what a text said on a date; deciding what that means for a
                situation is a lawyer's job.</div>
                """;
            return Results.Content(Page("How it works", body, null, "how",
                canonicalPath: "/how-it-works"), "text/html");
        });

        app.MapGet("/stories", () =>
        {
            var sb = new StringBuilder();
            sb.Append("""
                <p>These are real histories held by Lex. Counts come from the signed indexes as this page
                renders; every link lands on the evidence.</p>
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
                "Luxembourg's Covid-19 measures act. Rules on gatherings, masks and closures were rewritten again and again, "
                + "which is exactly when \"what did the rule say <i>that week</i>?\" stops being an academic question.",
                "How did the Luxembourg Covid-19 law change between July 2020 and July 2021?");

            Story("lu-legilux", "constitution-1868-10-17-n1",
                "A constitution, revised in public",
                "The Luxembourg constitution, from the early twentieth century to the 2023 reform, the same document, "
                + "re-consolidated after every revision, each state still retrievable.",
                "What changed in the Luxembourg constitution in 2023?");

            Story("eu-eurlex", "32013r0575",
                "Banking rules in waves",
                "The Capital Requirements Regulation, the rulebook a Luxembourg bank must apply. Its own Article 92 "
                + "(the capital ratios) has more than one lifetime.",
                "How has Article 92 of the CRR changed over its life?");

            Story("lu-legilux", "loi-1879-06-18-n1",
                "The criminal code is a moving target",
                "Luxembourg's penal code has been re-consolidated repeatedly in the last decade. Point-in-time matters most "
                + "where the question is what was punishable on the day of the act.",
                "Que disait le Code pénal luxembourgeois au 1er janvier 2020 ?");

            // The same cross-index aggregate that changes_in_period exposes: one ranking over the
            // selected corpus, never a Luxembourg-only list with EU bolted on elsewhere. Keep the
            // publisher beside each row because work slugs are only unique inside a publisher.
            var churn = readers
                .SelectMany(entry => entry.Value
                    .ChangesInPeriod("2020-03-01", "2021-07-01", null, byChurn: true, limit: 5)
                    .Select(change => new
                    {
                        Publisher = entry.Key,
                        Jurisdiction = entry.Value.Stamp.GetValueOrDefault("jurisdiction", entry.Key),
                        Change = change,
                    }))
                .OrderByDescending(row => row.Change.VersionsInPeriod)
                .ThenByDescending(row => row.Change.LastChange, StringComparer.Ordinal)
                .ThenBy(row => row.Publisher, StringComparer.Ordinal)
                .ThenBy(row => row.Change.GroupKey, StringComparer.Ordinal)
                .Take(5)
                .ToList();
            if (churn.Count > 0)
            {
                sb.Append("""
                    <div class="card"><h2 style="margin:0 0 4px">Which laws moved most during the pandemic</h2>
                    <p class="sub" style="margin:0 0 10px">March 2020 to July 2021, across every mounted jurisdiction,
                    ranked by how many new versions each law produced. Computed live, and available to your own code as
                    <span class="mono">changes_in_period(order="by_churn")</span>.</p><table>
                    <tr><th>law</th><th>jurisdiction</th><th>new versions</th><th></th></tr>
                    """);
                foreach (var row in churn)
                {
                    var c = row.Change;
                    sb.Append($"""
                        <tr><td><a href="/{H(row.Publisher)}/{H(c.GroupKey)}">{H(TitleShorten(c.Title) ?? c.GroupKey)}</a></td>
                        <td><span class="badge">{H(row.Jurisdiction)}</span></td>
                        <td class="mono">{c.VersionsInPeriod}</td>
                        <td><a href="/{H(row.Publisher)}/{H(c.GroupKey)}/diff/{H(c.FirstChange)}/{H(c.LastChange)}">what changed</a></td></tr>
                        """);
                }
                sb.Append("""
                    </table><p class="sub" style="margin:8px 0 0">
                    <a href="/?space=time&amp;from=2020-03-01&amp;until=2021-07-01&amp;order=by_churn"><b>Explore this period →</b></a></p></div>
                    """);
            }

            sb.Append("""
                <div class="card"><b>The honest half.</b> A demo that only shows wins is a brochure.
                  <a href="/lu-legilux/rgd-1998-08-03-n4/1900-01-01">Ask for a law in 1900</a> and Lex refuses,
                  with a reason code, instead of inventing a plausible text , 
                  <a href="/coverage">here is exactly what it holds and what it lacks</a>.</div>
                """);
            return Results.Content(Page("Watch the law move", sb.ToString(),
                "real histories from the Luxembourg and reviewed EU corpora, computed live", "find",
                canonicalPath: "/stories", assistant: true), "text/html");
        });

        return app;
    }

    public static bool BenchmarkClaimsMatchVerifiedManifests(
        RetrievalBenchmarkReport report, string collection,
        IReadOnlyCollection<VerifiedArtifactManifest> manifests)
    {
        var benchmarkFile = $"retrieval-benchmark-{collection}.json";
        var indexFile = $"index-{collection}.db";
        var benchmarkManifests = manifests.Where(item =>
            item.Artifacts.Contains(benchmarkFile, StringComparer.Ordinal)).ToArray();
        var indexManifests = manifests.Where(item =>
            item.Artifacts.Contains(indexFile, StringComparer.Ordinal)).ToArray();
        return benchmarkManifests.Length == 1
               && indexManifests.Length == 1
               && string.Equals(report.CodeCommit, benchmarkManifests[0].CodeCommit,
                   StringComparison.OrdinalIgnoreCase)
               && string.Equals(report.ManifestId, indexManifests[0].Sha256,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<RetrievalBenchmarkCase> LoadRetrievalCases()
    {
        using var stream = typeof(ExplainerEndpoints).Assembly.GetManifestResourceStream(
            "Lex.Web.retrieval-cases.json")
            ?? throw new InvalidOperationException("Embedded retrieval benchmark cases are missing.");
        return RetrievalBenchmarkCatalog.Load(stream);
    }

    private static RetrievalBenchmarkBaseline LoadRetrievalBaseline()
    {
        using var stream = typeof(ExplainerEndpoints).Assembly.GetManifestResourceStream(
            "Lex.Web.retrieval-baseline-v2.json")
            ?? throw new InvalidOperationException("Embedded retrieval benchmark baseline is missing.");
        return RetrievalBenchmarkCatalog.LoadBaseline(stream);
    }
}
