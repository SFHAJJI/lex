using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using Lex.Index;
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
                    string? description = null, string? lang = null)
            => PageShell.Page(ctx.PublicBase, title, body, subtitle, nav, h1, canonicalPath,
                              jsonLd, description, lang);
        var readers = ctx.Registry.All;
        var publicBase = ctx.PublicBase;
        var mcpCore = ctx.Mcp;
        var architecture = ArchitectureProgram.Registry;

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

        app.MapGet("/ai", (HttpRequest req) =>
        {
            var baseUrl = BaseUrl(req);
            // Counted, not written. See /developers for why.
            var tools = mcpCore.ToolDefs().OfType<JsonObject>()
                               .Select(t => t["name"]!.GetValue<string>()).ToList();
            var body = $$"""
                <p>Lex is <b>MCP-native</b>: you bring your AI, Lex brings the evidence. Your model asks the
                {{tools.Count}} Lex tools for the law as it stood on a date, and composes its answer from
                returned text, dates and hashes, Lex itself never interprets anything.</p>

                <h2>Connect in one line</h2>
                <div class="card"><b>Claude Code</b><pre class="mono" style="white-space:pre-wrap">claude mcp add --transport http lex {{baseUrl}}/mcp</pre></div>
                <div class="card"><b>Claude Desktop / any MCP client</b>, add a remote MCP server:
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

                <h2>The {{tools.Count}} tools</h2>
                <p class="sub">{{string.Join(" · ", tools)}} , 
                read-only, deterministic, every response carries its dates, its hash, and an honest refusal
                (<span class="mono">no_version_for_date</span>, <span class="mono">text_withheld</span>) when Lex cannot know.</p>

                <h2>Azure AI Foundry agents</h2>
                <div class="card">Foundry's Agent Service speaks remote MCP natively, point an agent at this
                endpoint and it gets all {{tools.Count}} tools (no key needed; leave approvals on for writes-free comfort):
                <pre class="mono" style="white-space:pre-wrap">{ "type": "mcp", "server_label": "lex", "server_url": "{{baseUrl}}/mcp", "require_approval": "never" }</pre></div>

                <p>Want to try it without installing anything? The capped <a href="/ask">built-in playground</a>
                runs the same tools. Prefer no AI at all? Everything is also a <a href="/">permalink</a>.</p>
                """;
            return Results.Content(Page("Use Lex with your AI", body,
                "your model + our evidence, MCP endpoint, one line to connect"), "text/html");
        });

        app.MapGet("/architecture", () =>
        {
            var cov = readers.Values.Select(r => r.Coverage()).OrderBy(c => c.Collection).ToList();
            var current = architecture.Current;
            var mountedSchemas = string.Join(", ", cov.Select(c => c.Stamp.GetValueOrDefault("schema", "unknown"))
                                                     .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            var coverageRows = string.Join("", cov.Select(c => $"""
                <tr><td>{H(c.Collection)}</td><td class="mono">{c.Groups:n0}</td>
                <td class="mono">{c.Rows:n0}</td><td class="mono">{H(c.Stamp.GetValueOrDefault("schema"))}</td>
                <td class="mono">{H(c.Stamp.GetValueOrDefault("corpus_commit"))}</td></tr>
                """));
            var body = ArchitectureTabs("current") + $"""
                <p class="lede">This page describes the system serving requests now. Target-state work is kept
                separately on <a href="/architecture/next">Next</a>.</p>
                <div class="card"><table class="kv">
                <tr><th>retrieval</th><td>{H(current.Retrieval)}, deterministic FTS5/BM25</td></tr>
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
                <div class="card"><table><tr><th>collection</th><th>works</th><th>versions</th><th>schema</th><th>corpus commit</th></tr>
                {coverageRows}</table></div>
                <h2>Contracts preserved</h2>
                <p>Exact publisher text, hashes, anchors, timelines, refusals, comparisons and diffs remain
                authoritative. The backend returns structured MCP JSON and the workspace renders the separate
                <span class="mono">UiEffect</span> field.</p>
                """ + """
                <p>Lex answers one question, <b>what did the rule say on that date?</b>, for Luxembourg law and the selected EU works in the mounted index,
                in a way a developer can build on and an auditor can check. Everything below is open source and open data.</p>

                <h2>Two layers, one hash chain</h2>
                <div class="card"><pre class="mono" style="white-space:pre-wrap;font-size:12.5px;margin:0">EVIDENCE LAYER (append-only, verbatim)          CONSUMPTION LAYER (regenerable, clean)
                lex-corpus-lu-legilux   lex-corpus-eu-eurlex   lex-articles
                the exact bytes the state published       →   per-ARTICLE Markdown + JSON
                sha256 per file, observation chains            stable publisher-minted anchors
                                                               validity intervals per provision
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
                per provision. "What did Article 92 say over its life?" is a file read: every distinct text as a validity
                interval, plus mechanically detected renumberings (identical-hash matching, never interpretation).</p>

                <h2>Honesty as an API contract</h2>
                <div class="card"><table>
                <tr><th>refusal status</th><th>meaning</th></tr>
                <tr><td class="mono">no_version_for_date</td><td>the work exists; no version was valid on that date</td></tr>
                <tr><td class="mono">unknown_work / unknown_anchor</td><td>Lex does not hold it, and says so</td></tr>
                <tr><td class="mono">anchor_not_in_version</td><td>that article did not exist in that version (knowing this IS the product)</td></tr>
                <tr><td class="mono">text_withheld</td><td>metadata held, text gate not cleared; official link provided</td></tr>
                <tr><td class="mono">outside_observed_window</td><td>before the observation history begins</td></tr>
                </table></div>
                <p>A flagged wrong answer is still wrong, so Lex refuses instead; <a href="/coverage">coverage</a> exists to
                state what we do <b>not</b> have. The AI layer (<a href="/">the front page</a>) is additive and separated:
                one model + system prompt over the same in-process tool core the public <span class="mono">/mcp</span> serves
                ,  parity by construction; no framework, no interpretation (fitness rule F10).</p>

                <h2>Build on it</h2>
                <p>
                <a href="https://github.com/SFHAJJI/lex-articles">lex-articles</a>, machine-readable corpus (CC-BY, SCHEMA.md contract) ·
                <a href="https://github.com/SFHAJJI/lex">lex</a>, all code, Apache-2.0, incl. the
                <a href="https://github.com/SFHAJJI/lex/blob/main/docs/lex-spec-v4.md">full decision record (D1, D47)</a> ·
                <a href="https://github.com/SFHAJJI/lex-corpus-lu-legilux">evidence repos</a> ·
                hosted MCP: <span class="mono">claude mcp add --transport http lex https://law.soufien.lu/mcp</span></p>
                """;
            body = body.Replace("MOUNTED_INDEX_SCHEMAS", H(mountedSchemas), StringComparison.Ordinal);
            return Results.Content(Page("Architecture", body,
                "what is deployed now, read separately from what comes next"), "text/html");
        });

        app.MapGet("/architecture/next", () =>
        {
            var rows = string.Join("", architecture.Milestones.Select(m => $"""
                <tr><td class="mono">{H(m.Id)}</td><td><b>{H(m.Title)}</b><br><span class="sub">{H(m.Outcome)}</span></td>
                <td>{StatusBadge(m.Status)}</td></tr>
                """));
            var body = ArchitectureTabs("next") + $"""
                <p class="lede">The accepted target architecture, with status read from the registry committed
                beside the implementation. Nothing on this page is implied to be live.</p>
                <div class="card"><table><tr><th>milestone</th><th>outcome</th><th>status</th></tr>{rows}</table></div>
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
                "the accepted target, its gates, and what has actually shipped"), "text/html");
        });

        app.MapGet("/benchmarks", () =>
        {
            static string F(double value, string format) =>
                value.ToString(format, CultureInfo.InvariantCulture);
            var b = architecture.Baseline;
            var retrievalCases = RetrievalBenchmarkCatalog.Create();
            var benchmarkPath = Path.Combine(ctx.Options.IndexDir, "retrieval-benchmark-eu-eurlex.json");
            RetrievalBenchmarkReport? report = null;
            try
            {
                if (File.Exists(benchmarkPath)
                    && ctx.Registry.IsArtifactVerified(Path.GetFileName(benchmarkPath)))
                    report = JsonSerializer.Deserialize<RetrievalBenchmarkReport>(File.ReadAllBytes(benchmarkPath),
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            }
            catch (Exception)
            {
                // A benchmark is evidence, not a startup dependency. Invalid evidence is omitted.
            }
            var caseRows = string.Join("", retrievalCases.GroupBy(c => c.Category)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"<tr><td>{H(g.Key)}</td><td class=\"mono\">{g.Count()}</td></tr>"));
            var relevance = report is null
                ? """
                  <div class="notice"><b>Not measured yet.</b> The public 200-case suite is a gated milestone.
                  Hybrid will not become default until exact identifiers, temporal isolation, nDCG@10,
                  regression, latency and memory pass their recorded thresholds.</div>
                  """
                : $"""
                  <div class="notice"><b>Latest gate: {(report.ActivationGatePassed ? "passed" : "not passed")}.</b>
                  Review status: {H(report.ReviewStatus)}. Measured {H(report.Timestamp)} over
                  {report.SampleCount:n0} public cases.</div>
                  <div class="card"><table>
                  <tr><th>measure</th><th>keyword</th><th>hybrid</th></tr>
                  <tr><td>MRR</td><td class="mono">{F(report.Keyword.Mrr, "0.000")}</td><td class="mono">{F(report.Hybrid.Mrr, "0.000")}</td></tr>
                  <tr><td>Recall@10</td><td class="mono">{F(report.Keyword.RecallAt10, "0.000")}</td><td class="mono">{F(report.Hybrid.RecallAt10, "0.000")}</td></tr>
                  <tr><td>nDCG@10</td><td class="mono">{F(report.Keyword.NdcgAt10, "0.000")}</td><td class="mono">{F(report.Hybrid.NdcgAt10, "0.000")}</td></tr>
                  <tr><td>warm p95</td><td class="mono">{F(report.Keyword.P95Ms, "0.0")} ms</td><td class="mono">{F(report.Hybrid.P95Ms, "0.0")} ms</td></tr>
                  <tr><td>warm p99</td><td class="mono">{F(report.Keyword.P99Ms, "0.0")} ms</td><td class="mono">{F(report.Hybrid.P99Ms, "0.0")} ms</td></tr>
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
                Each case names its language, time scope, relevant work, explanation and review status.</p>
                <h2>Publication rule</h2>
                <p>Future reports name code and corpus commits, the signed artifact manifest, embedding model,
                machine or Azure resource, timestamp, sample count and review status. Initial relevance
                judgments are labelled engineer-reviewed until a lawyer reviews them.</p>
                """;
            return Results.Content(Page("Benchmarks", body,
                "measured retrieval, latency, memory, index size and cost evidence"), "text/html");
        });

        app.MapGet("/benchmarks/latest.json", () =>
        {
            var path = Path.Combine(ctx.Options.IndexDir, "retrieval-benchmark-eu-eurlex.json");
            return File.Exists(path) && ctx.Registry.IsArtifactVerified(Path.GetFileName(path))
                ? Results.File(path, "application/json")
                : Results.NotFound(new { status = "not_measured_yet" });
        });

        app.MapGet("/benchmarks/cases.json", () => Results.Json(
            RetrievalBenchmarkCatalog.Create(), new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
                WriteIndented = true,
            }));

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
                <p class="sub">Extraction profiles are immutable (<span class="mono">akn-lu/1</span>, <span class="mono">xhtml-eu/1</span>);
                a citation pinned under a profile verifies under that profile, forever. Contract:
                <a href="https://github.com/SFHAJJI/lex-articles/blob/main/SCHEMA.md">SCHEMA.md</a>.</p>
                """;
            return Results.Content(Page("Verify", body,
                "the signature, the hash chain, and how to check both without trusting us"), "text/html");
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
                issued, when the breach happened. Official publishers do hold dated consolidated editions , 
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
                  SIGNED SQLITE INDEX (ECDSA P-256) ◄─────┘
                        │
                        ├──► MCP server  (public, no key)      
                        └──► this site   (Container Apps, scale-to-zero)</pre></div>
                <p class="sub">Azure: Container Apps behind a managed certificate, Container Registry, Azure
                OpenAI (gpt-5-mini) for the assistant, Application Insights via OpenTelemetry, Azure DNS.
                The web app runs at 0.25 vCPU and <b>scales to zero</b>, idle cost is essentially the
                registry and the DNS zone.</p>

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
                <tr><td><b>A small model, tightly fenced</b></td>
                    <td>The assistant only picks lookups and quotes results, no agent framework, no chain of
                    reasoning over law. Cheap, auditable, and wrong answers are visible against the evidence
                    shown beside them.</td></tr>
                <tr><td><b>Nightly commits nothing when unsure</b></td>
                    <td>A &gt;5% drop in works, or a re-derivation that is not byte-identical, aborts the run.
                    A partial upstream response must never rewrite history.</td></tr>
                </table>
                <p class="sub" style="margin:8px 0 0">Forty-eight numbered decisions like these are recorded in
                the specification, each with its rationale, so "why did you do it that way" has a written
                answer rather than a recollection.</p></div>

                <h2>The machinery that keeps it fresh</h2>
                <p>Law changes while you sleep, so the corpus is rebuilt while I do. One scheduled job at
                02:17 UTC drives the whole fleet, no manual step exists, and there is deliberately only one
                credential and one cron for all publishers.</p>
                <div class="card"><table>
                <tr><th>stage</th><th>what it does, and how it refuses to do damage</th></tr>
                <tr><td><b>1. Ingest</b></td><td>Asks each publisher what versions exist, downloads any it has
                not seen, and writes them <i>verbatim</i>. Existing files are never reopened for writing , 
                the evidence layer is append-only by construction.</td></tr>
                <tr><td><b>2. Anomaly gate</b></td><td>If the work count drops more than 5%, the run assumes the
                upstream response was partial, discards everything and commits nothing. A bad night leaves
                yesterday's good data in place.</td></tr>
                <tr><td><b>3. Derive</b></td><td>Regenerates the per-article layer from the verbatim files.</td></tr>
                <tr><td><b>4. Determinism guard</b></td><td>If derived output changed while no source file did,
                that means the extractor is non-deterministic, the run fails loudly and commits nothing,
                because a silent extraction drift would corrupt history.</td></tr>
                <tr><td><b>5. Index &amp; publish</b></td><td>Rebuilds the search index, signs it (ECDSA P-256),
                publishes it as a release asset, regenerates the JSONL and Parquet datasets.</td></tr>
                <tr><td><b>6. Report</b></td><td>Writes a three-state outcome per publisher
                (<span class="mono">ran_committed</span> / <span class="mono">ran_no_change</span> /
                <span class="mono">failed_*</span>) and opens an issue on failure.</td></tr>
                </table></div>
                <p>The result travels with the data: every index carries a signed stamp recording when it was
                built and from which corpus commit, and every tool response returns it. <b>This is that stamp,
                read live from the running indexes:</b></p>
                <div class="card"><table>
                <tr><th>publisher</th><th>index built</th><th>from corpus commit</th><th>signature</th></tr>
                {string.Join("", readers.Values.OrderBy(r => r.Collection, StringComparer.Ordinal).Select(r => $"""
                    <tr><td>{H(r.Collection)}</td>
                    <td class="mono">{H(r.Stamp.GetValueOrDefault("built_at"))}</td>
                    <td class="mono">{H(r.Stamp.GetValueOrDefault("corpus_commit"))}</td>
                    <td>{(r.SignatureValid ? "<span class=\"badge ok\">valid</span>" : "<span class=\"badge warn\">unsigned</span>")}</td></tr>
                    """))}
                </table>
                <p class="sub" style="margin:8px 0 0">Nothing here is typed by hand, if the pipeline stopped,
                this table would say so. The same values come back from the
                <a href="/developers">coverage tool</a> in every API response.</p></div>

                <h2>What broke, and what it taught</h2>
                <div class="card">
                <p><b>A silently dead search, caught in production.</b> The nightly job built the search index
                without the per-article layer. Nothing errored: the index was valid, signed, and published , 
                it just had zero provisions in it, so search returned nothing. The automated eval suite caught
                it by asking a question a user would ask and noticing the assistant could no longer find a
                Luxembourg code.</p>
                <p class="sub">Fix: the index step now runs after derivation and takes the article layer as a
                required input, so the failure cannot recur. Lesson: a green build is not a working system , 
                the only tests that would have caught this are the ones that exercise it end to end, the way
                someone actually uses it.</p>
                <p><b>A parser that quietly duplicated text.</b> Adding Formex XML support introduced doubled
                paragraphs where an article had introductory text followed by a list. It looked plausible on
                screen. It was caught by re-reading real output rather than trusting a passing test, fixed the
                same day, and pinned with a fingerprint test so the profile can never drift again.</p>
                </div>

                <h2>How correctness is proven, not claimed</h2>
                <div class="card"><table>
                <tr><th>mechanism</th><th>what it guarantees</th></tr>
                <tr><td>34 unit tests</td><td>parsers, temporal logic, index schema, signing</td></tr>
                <tr><td>Frozen profile fingerprints</td><td>a published extraction can never change output</td></tr>
                <tr><td>Determinism guard in CI</td><td>re-derivation is byte-identical or the run commits nothing</td></tr>
                <tr><td>10 end-to-end AI evals</td><td>the assistant picks the right tools and never cites a source it was not given</td></tr>
                <tr><td>LLM-judged groundedness</td><td>answers scored against the evidence actually returned</td></tr>
                <tr><td>ECDSA-signed indexes</td><td>anyone can verify a build was not altered, <a href="/verify">recipe</a></td></tr>
                </table></div>

                <h2>Scale</h2>
                <p><span class="badge">{cov.Sum(c => c.Groups):n0} laws</span>
                <span class="badge">{cov.Sum(c => c.Rows):n0} dated versions</span>
                <span class="badge">333,000+ articles indexed</span>
                <span class="badge">~7,200 lines of C# across 10 projects</span>
                <span class="badge">3 XML/HTML dialects parsed</span>
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
                <p>Lex is built and run by <b><a href="/about">Soufien Hajji</a></b>, a senior .NET, Azure
                and AI engineer in Luxembourg. It is a personal project, unaffiliated with any publisher or
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
            return Results.Content(Page("How it was built", body, null, "how"), "text/html");
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
            var versions = cov.Sum(c => c.Rows);
            var body = $$"""
                <p class="lede">I build systems that have to be right rather than plausible: regulated
                platforms where an answer has to carry its source, its date, and a way to check it.</p>

                <div class="card">
                <p><b>Soufien Hajji</b>, senior .NET, Azure and AI engineer, based in Luxembourg. Nine
                years across telecoms, rail, investment banking and asset management, most of it on
                systems where being wrong is expensive: front-office trading platforms, regulatory-capital
                reporting, and the agentic AI layer on top of it.</p>
                <p class="sub">Lex is a personal project, unaffiliated with any publisher or public body.
                It is built the way I build professionally, which is the reason it exists: the pipeline is
                deterministic, the claims are testable, and the parts that cannot be verified say so
                instead of guessing.</p>
                <p><a class="pick main" href="https://api.soufien.lu/cv/en" rel="noopener">CV, English (PDF)</a>
                &nbsp; <a class="pick" href="https://api.soufien.lu/cv/fr" rel="noopener">CV, français (PDF)</a>
                &nbsp; <a class="pick" href="https://www.linkedin.com/in/soufien-hajji" rel="noopener">LinkedIn ↗</a></p>
                </div>

                <h2>Three things, built the same way</h2>

                <div class="card">
                <b><a href="https://law.soufien.lu">law.soufien.lu</a></b> &middot; this one
                <p class="sub">Point-in-time Luxembourg law and selected EU law: {{works:n0}} works as
                {{versions:n0}} dated versions, a public MCP endpoint, open datasets, and a signed index
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
                "Senior .NET, Azure and AI engineer in Luxembourg. Lex is one of three systems built the same way.",
                "about"), "text/html");
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
                <div class="card"><table><tr><th>decision</th><th>choice, alternative and cost</th><th>status</th></tr>
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
                something, never when the law applied. Last night's ingest added versions that took effect
                on 1 January 2026 and versions that take effect years from now. Git timestamps both with
                last night. <span class="mono">git log</span> answers "when did we find out", which is a
                real question, but it is not the question this product exists for.</p>

                <p><b>2. Law is dated into the future.</b> Lex holds versions valid to
                <span class="mono">{{H(latest)}}</span>. Publishers routinely issue today a text that
                becomes binding years from now. Git cannot express a commit that becomes true later, so a
                git-as-history model must either publish future law as if it were current, or drop it.
                Both are wrong answers to "what is in force today".</p>

                <p><b>3. Publishers backfill.</b> A consolidation covering 2019 can be issued in 2026.
                Under git-as-history that arrives as a 2026 change to the law, which is simply false. The
                fix is to record two things separately: when the text applied, and when we saw it. That is
                two columns, and it is not something a commit graph can carry.</p>

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
                "how"), "text/html");
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
                <p class="lede">Lex is MCP-native: you bring the model, Lex brings the evidence.
                {{tools.Count}} read-only tools over signed indexes, no key, no account, no rate limit on
                the endpoint.</p>

                <h2>Connect</h2>
                <div class="card"><b>Claude Code</b>
                <pre class="mono" style="white-space:pre-wrap;margin:6px 0 0">claude mcp add --transport http lex {{baseUrl}}/mcp</pre></div>
                <div class="card"><b>Claude Desktop, Cursor, any MCP client</b>
                <pre class="mono" style="white-space:pre-wrap;margin:6px 0 0">{ "mcpServers": { "lex": { "url": "{{baseUrl}}/mcp" } } }</pre></div>
                <div class="card"><b>Azure AI Foundry Agent Service</b>, remote MCP is native:
                <pre class="mono" style="white-space:pre-wrap;margin:6px 0 0">{ "type": "mcp", "server_label": "lex", "server_url": "{{baseUrl}}/mcp", "require_approval": "never" }</pre></div>
                <div class="card"><b>No framework at all</b>, it is JSON-RPC over one POST:
                <pre class="mono" style="white-space:pre-wrap;margin:6px 0 0">curl -X POST {{baseUrl}}/mcp -H 'Content-Type: application/json' \
                  -d '{ "jsonrpc":"2.0", "id":1, "method":"tools/call",
                        "params": { "name":"as_of",
                          "arguments": { "work":"eu-eurlex:32016r0679",
                            "date":"2019-03-15", "mode":"select", "anchors":"art_33" } } }'</pre></div>

                <h2>The {{tools.Count}} tools</h2>
                <div class="card"><table>
                <tr><th>tool</th><th>arguments</th><th>answers</th></tr>
                <tr><td class="mono">as_of</td><td class="mono">work, date, [language], [mode: full\|outline\|select], [anchors]</td>
                    <td>the text in force on a date. <span class="mono">outline</span> lists article anchors only;
                    <span class="mono">select</span> returns just the anchors you name, use it, codes are large.</td></tr>
                <tr><td class="mono">article_history</td><td class="mono">work, anchor</td>
                    <td>every distinct text one article has had, as validity intervals, plus renumbering events.</td></tr>
                <tr><td class="mono">timeline</td><td class="mono">work</td><td>all versions of a work with their validity windows.</td></tr>
                <tr><td class="mono">diff</td><td class="mono">work, from_date, to_date, [language]</td><td>what changed between two versions.</td></tr>
                <tr><td class="mono">in_force_on</td><td class="mono">date, [publisher], [document_type], [limit], [offset]</td>
                    <td>everything that applied on a given day.</td></tr>
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
                <tr><td class="mono">changes_in_period</td><td class="mono">from_date, to_date, [publisher], [document_type], [order], [limit], [offset]</td>
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
                <span class="mono">outside_observed_window</span>. Build against them: an empty result and a
                refusal are different things.</p>

                <h2>Or skip the API, take the data</h2>
                <p>Every provision of every version, one row each, licence and attribution inline.
                {{cov.Sum(c => c.TextServed):n0}} versions carry full text.</p>
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
                        method: 'POST', headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'tools/call',
                                               params: { name: tool.value, arguments: parsed } })
                      });
                      const j = await r.json();
                      const text = j.result && j.result.content && j.result.content[0]
                        ? j.result.content[0].text : JSON.stringify(j, null, 2);
                      let pretty; try { pretty = JSON.stringify(JSON.parse(text), null, 2); } catch { pretty = text; }
                      out.textContent = pretty.length > 6000 ? pretty.slice(0, 6000) + '\n… truncated for display' : pretty;
                    } catch (err) { out.textContent = 'request failed: ' + err.message; }
                  });
                })();
                </script>
                """;
            return Results.Content(Page("For developers", body, null, "dev"), "text/html");
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
                The assistant on the front page is a small model whose only job is to pick the right lookup
                and quote what comes back, every answer shows the evidence underneath it, so you can check
                the answer against the source without leaving the page.
                <a href="/lu-legilux/rgd-1998-08-03-n4/1900-01-01">Watch it refuse →</a></p>

                <h2>What it holds today</h2>
                <p><span class="badge">{cov.Sum(c => c.Groups):n0} laws</span>
                <span class="badge">{cov.Sum(c => c.Rows):n0} dated versions</span>
                <span class="badge">{cov.Sum(c => c.TextServed):n0} with full text</span>
                <span class="badge">refreshed nightly</span></p>
                <p>Lex holds every Luxembourg law the state maintains a consolidated edition for, plus a set
                of EU financial and data regulations. Roughly 24,000 Luxembourg acts never receive a
                consolidated edition and are not here, and the gaps are published rather than hidden.
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
            return Results.Content(Page("How it works", body, null, "how"), "text/html");
        });

        app.MapGet("/stories", () =>
        {
            var sb = new StringBuilder();
            sb.Append("""
                <p>Point-in-time retrieval sounds abstract until you watch a law move. These are real
                histories held by Lex, every number below is computed from the signed indexes as this
                page renders, and every link lands on the evidence.</p>
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

            // The same aggregate that changes_in_period exposes: the page and the API answer this
            // from one code path, so the site never knows something an MCP client cannot get.
            if (readers.TryGetValue("lu-legilux", out var luR))
            {
                var churn = luR.ChangesInPeriod("2020-03-01", "2021-07-01", null, byChurn: true, limit: 5);
                if (churn.Count > 0)
                {
                    sb.Append("""
                        <div class="card"><h2 style="margin:0 0 4px">Which laws moved most during the pandemic</h2>
                        <p class="sub" style="margin:0 0 10px">March 2020, July 2021, ranked by how many new versions
                        each law produced. Computed live, and available to your own code as
                        <span class="mono">changes_in_period(order="by_churn")</span>.</p><table>
                        <tr><th>law</th><th>new versions</th><th></th></tr>
                        """);
                    foreach (var c in churn)
                        sb.Append($"""
                            <tr><td><a href="/lu-legilux/{H(c.GroupKey)}">{H(TitleShorten(c.Title) ?? c.GroupKey)}</a></td>
                            <td class="mono">{c.VersionsInPeriod}</td>
                            <td><a href="/lu-legilux/{H(c.GroupKey)}/diff/{H(c.FirstChange)}/{H(c.LastChange)}">what changed</a></td></tr>
                            """);
                    sb.Append("""
                        </table><p class="sub" style="margin:8px 0 0">
                        <a href="/changed?from=2020-03-01&amp;to=2021-07-01&amp;order=by_churn"><b>Explore any period →</b></a></p></div>
                        """);
                }
            }

            sb.Append("""
                <div class="card"><b>The honest half.</b> A demo that only shows wins is a brochure.
                  <a href="/lu-legilux/rgd-1998-08-03-n4/1900-01-01">Ask for a law in 1900</a> and Lex refuses,
                  with a reason code, instead of inventing a plausible text , 
                  <a href="/coverage">here is exactly what it holds and what it lacks</a>.</div>
                """);
            return Results.Content(Page("Stories, watch the law move", sb.ToString(),
                "real histories from the Luxembourg and selected EU corpora, computed live", "find"), "text/html");
        });

        return app;
    }
}
