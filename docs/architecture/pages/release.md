# Release

Code, legal data, retrieval artifacts and assistant behavior are one release unit. A deployment is
not production merely because a container started.

![A signed artifact set becomes a zero-traffic candidate, passes health, retrieval, assistant and browser gates, then a separate signed promotion moves traffic while retaining the exact former production revision.](/built/diagrams/release.svg)

[Open the release diagram at full size](/built/diagrams/release.svg)

| Box | Responsibility | Lives in |
|---|---|---|
| Signed artifacts | Bind code, indexes, vectors, encoder and scope to exact hashes | local builder plus bounded `lex-ops` publication |
| Candidate | Start an immutable revision while production traffic remains unchanged | `deploy.yml` and Azure Container Apps |
| Separated gates | Verify readiness, retrieval, assistant behavior, browser behavior and human review | candidate scripts, signed evaluation release and protected environments |
| Promote or restore | Revalidate identities before one traffic change or exact rollback | `revision-traffic.yml` and Key Vault receipts |
| Retention boundary | Preserve production and one rollback without deleting unproven shared artifacts | Container Apps revision policy and `lex-ops` private-staging cleanup |

## State machine

| State | Active production | Inactive records | Allowed next action |
|---|---|---|---|
| Steady | Current revision at 100 percent | Exact former production as rollback | Create one candidate |
| Waiting | Current revision at 100 percent | Rollback plus one zero-traffic candidate | Evaluate, promote or reconcile |
| Promoting | Evaluated candidate activated at zero traffic | Previous authorities still pinned | Revalidate evidence, then move traffic |
| New steady | New production at 100 percent | Exact former production as rollback | Emit and verify release-state receipt |

> **Why this design?** A zero-traffic candidate can be tested against production-shaped services
> without changing the reader's route. Keeping the exact former production revision makes rollback
> an identity selection, not a rebuild that might fetch different bytes.
>
> ```text
> identities != signed_evaluation ? leave_traffic_unchanged : promote_once_and_retain_previous
> ```

`maxInactiveRevisions` is one in steady state and two only while a candidate waits. Azure Container
Apps has no supported per-revision delete. An unresolved newer candidate cannot be discarded by
lowering the limit without risking the older valid rollback, so cleanup follows identity and
creation order rather than age.

## Evidence before traffic

The deploy workflow creates a zero-traffic revision from the exact image and signed artifact set,
then checks readiness, Model Context Protocol (MCP), Luxembourg and European Union (EU) search. The
assistant evaluation binds the frozen cases, candidate, typed outcomes, latency and cost to a
signed report under separate project-owner review. Only the traffic workflow may revalidate those
records and promote; no automated actor approves its own output.

This page does not hard-code mounted state: runtime evidence supplies the active identities and
capabilities. The traffic workflow also requires target and rollback to remain at `min=max=1`;
quotas, idempotency and thread state must be externalized before multiple replicas are allowed.

## Release evidence chain

This is the complete continuous integration and continuous delivery (CI/CD) chain at the level where
an operator can decide whether traffic is safe. The detailed workflow and script mechanics stay in
the linked runbooks rather than being copied into this dossier.

**Status legend:** **Built** means the implementation and deterministic tests exist; **Measured**
means the named frozen artifact or result was reproduced; **Quarantined** means activation is
denied by its gate; **Next** means exact-release evidence or an operator action is still outstanding.
These labels describe the evidence cited here, not a claim about an uninspected live revision.

| Stage | Status | Input identity | Gate | Evidence and signature boundary | Failure behavior |
|---|---|---|---|---|---|
| Publisher acquisition | **Measured** | Exact EUR-Lex or Legilux response, requested publisher identity and observation time | Bounded retries, complete enumeration and typed acquisition issues | Corpus inventory binds acquired files, hashes and source coordinates; this is integrity evidence, not a detached signature | Keep the accepted corpus; incomplete enumeration cannot create removals or publish. |
| Corpus release | **Measured** | Immutable raw records plus the exact adapter/code commit | Integrity, expected inventory, provenance and publisher-specific completeness checks | Protected corpus Git commit/ref plus its integrity manifest binding files, hashes and source coordinates | Refuse the corpus release and retain its predecessor. |
| Derived articles | **Measured** | Exact corpus commit, immutable extraction profile and deriver commit | Deterministic re-derive, schema checks and extraction-quality guards | Protected articles Git commit/ref plus its integrity manifest binding corpus, profile, code and derived-file hashes | Refuse the article layer; source evidence is never rewritten to hide a gap. |
| SQLite and vectors | **Measured** | Exact articles commit, index code, encoder/tokenizer and build configuration | Stamp verification, content/ordinal checks, required publishers and reproducible artifact hashes | Build tickets, index stamps and hashes bind database, vectors, model bundle, scope and commits; no detached release signature exists yet | Do not mount or stage a mismatched artifact set. |
| 200-case deterministic retrieval benchmark | **Quarantined** for Luxembourg (LU); **Next** for EU | Frozen engineer-reviewed cases, exact index/vector/model identities and machine envelope | Exactness, temporal, relevance, latency, memory and regression thresholds; **no large language model (LLM) grader** | Benchmark report and digest become inputs to the later signed artifact release | Keyword remains the default: LU hybrid failed its accepted evidence gate and exact-release EU hybrid remains unproven. |
| Private staging | **Next** | The exact locally verified release bytes, hashes and create-only destination names | Create each private object once, then bind its entity tag (ETag), hash and byte length; this is coordination evidence, not immutable storage | Draft inventory and storage ETags record the exact staged objects; staging is not public signed evidence | Delete only the exact failed draft objects the publisher created; never a canonical release. |
| Signed release | **Next** | Exact staged bytes, artifact identities and publication workflow commit | An **exact-byte publication transaction** signs the manifest, publishes once, downloads every public asset and compares the Secure Hash Algorithm 256-bit (SHA-256) digest plus length | This final artifact publication is the first detached-signature boundary: immutable public release, whole-artifact manifest and signature | Ambiguous read-back requires reconciliation; retry never overwrites an existing public release. |
| Zero-traffic candidate | **Next** | Exact code commit, immutable image digest and signed artifact set | Readiness, attestation, both jurisdictions, Hypertext Transfer Protocol (HTTP) contract and one-replica invariant | Azure Resource Manager plus runtime attestation bind revision, image, code and the already signed mounted manifest set | Candidate remains at zero traffic; production and exact rollback are unchanged. |
| 25-case live assistant evaluation | **Next** | Frozen reviewed catalog, exact candidate, Azure candidate deployment and separate grader deployment | Real plan/freeze/execute requests, typed result checks, token/cost/latency and browser gates, with a reported relevance score beside each repetition | Catalog review is separately signed; report and browser-evidence hashes are bound by the signed evaluation release | Any repetition or budget failure denies promotion and returns the candidate to zero traffic. |
| Promotion or rollback | **Next** | Exact candidate evidence release plus current production/rollback identities | Protected workflow revalidates every identity immediately before one traffic mutation | Signed promotion and release-state receipts | No identity match means no traffic change; rollback selects the exact retained former production revision. |

The offline retrieval benchmark and live assistant evaluation answer different questions. The first
uses 200 fixed judgments and deterministic metrics to decide whether hybrid retrieval may activate;
it does not call an answer model or a model grader. The second uses 25 natural-language cases against
the real Azure-backed candidate and a separately authenticated release grader to test planning,
typed execution and bounded final responses.

> **Why this design?** Keyword retrieval remains the production default because a semantic model is
> a release artifact, not a configuration preference. Hybrid mounts only when the exact signed
> database, vectors, encoder and benchmark evidence pass together; a failed or missing result keeps
> the capability quarantined without weakening deterministic search.

> **Why this design?** Every stage pins exact commits, digests and byte lengths because a tag, cache
> hit or protected branch can move after a build starts. Navigation may follow `main`; release
> authority comes only from the immutable identities recorded in signed evidence.

## One article, followed end to end

This is the whole claim of the product in one table. It follows a single provision, Article 6 of the
General Data Protection Regulation, from the bytes the publisher served through to the citation the
interface renders. Every step carries the SHA-256 of what it produced, so any link in the chain can
be recomputed instead of believed, and the right-hand column states what that step does not prove.

It was reproduced from the frozen EU database whose file SHA-256 is
`f827e089bddff64709926af4341bc0ddbfbef829a5c3e29400754aec3b649fd9`. It is measured local evidence
for that exact build, not a claim about any signed release.

| Trace step | Status and exact evidence | Source and test | Limitation |
|---|---|---|---|
| Publisher bytes | **Measured.** The held English expression came from [EUR-Lex](https://eur-lex.europa.eu/legal-content/EN/TXT/?uri=CELEX:02016R0679-20160504); its current observed body SHA-256 is `28524c5589d9c80dee357fe96498302b4fefb29b3cc9ada7dcad52c967e3f15c`. | [`EurLexAdapter.FetchBody`](https://github.com/SFHAJJI/lex/blob/main/src/Lex.Sources.EurLex/EurLexAdapter.cs); [`EurLexScopeTests`](https://github.com/SFHAJJI/lex/blob/main/tests/Lex.Tests/EurLexScopeTests.cs) | The official source remains authoritative; acquisition proves what Lex observed, not legal effect. See [data authority](https://github.com/SFHAJJI/lex/blob/main/docs/architecture/pages/data.md). |
| Corpus version | **Measured.** Corpus commit `e9c4df0981c855855a1a28218cf086ddeb5bb691`, manifest SHA-256 `eb5abe73c30f7d50decc71539e0e39a76ebb3b1da437b69fd9e3ab38c65a4bd0`, exact version `eu-eurlex:32016r0679:2016-05-04--af3e8edcc8aeb9b8c10e891880377cb0b363a8fa7005a1b45557d21afa592de5`. | [`CorpusWriter.WriteAsync`](https://github.com/SFHAJJI/lex/blob/main/src/Lex.Ingest/CorpusWriter.cs); [`CorpusIntegrityTests`](https://github.com/SFHAJJI/lex/blob/main/tests/Lex.Tests/CorpusIntegrityTests.cs) | The manifest is integrity-bound in a protected corpus commit; it is not a detached artifact signature. |
| Derived article | **Measured.** Articles commit `ac8851147534b9addfa2231c4364cbd785065841`, profile `xhtml-eu/1`, anchor `art_6`, Article 6 text SHA-256 `dffea205327743e03f21c6910a899b7bfc081e40905defd085ab9d52dbb3fc87`. | [`DeriveWriter.Derive`](https://github.com/SFHAJJI/lex/blob/main/src/Lex.Derive/DeriveWriter.cs); [`XhtmlEuTests`](https://github.com/SFHAJJI/lex/blob/main/tests/Lex.Tests/XhtmlEuTests.cs) | Derivation can expose only safely structured text; [known defects](https://github.com/SFHAJJI/lex/blob/main/docs/known-defects.md) retain image-only and structural gaps. |
| Index row and text blob | **Measured.** Builder commit `27f0e02cb0da8e0fdf9f8322d3eef3b3ae09c776`, content digest `158bf28e9cfe5facefe5b728ba221f6d00162b101f79b5d59b937695d4ea20f1`, record SHA-256 `44d09ee49e187e02cf8649106b90badc16600d8227eb1f6f851b2054775bcf84`; the `art_6` row addresses the text blob by the article digest above. | [`IndexFromCorpus.Build`](https://github.com/SFHAJJI/lex/blob/main/src/Lex.Ingest/IndexFromCorpus.cs) and [`IndexBuilder.Build`](https://github.com/SFHAJJI/lex/blob/main/src/Lex.Index/IndexBuilder.cs); [`IndexTests`](https://github.com/SFHAJJI/lex/blob/main/tests/Lex.Tests/IndexTests.cs) | The database stamp is verified build provenance; the first detached release-signature boundary is final artifact publication. |
| Typed legal operation | **Measured.** `as_of(work=eu-eurlex:32016r0679, date=2021-01-01, language=en, mode=select, anchors=art_6)` returned `ok`, the exact version above and one provision at `art_6`. | [`McpCore.CallToolAsync`](https://github.com/SFHAJJI/lex/blob/main/src/Lex.Mcp/McpCore.cs); [`McpContractTests`](https://github.com/SFHAJJI/lex/blob/main/tests/Lex.Tests/McpContractTests.cs); [frozen-data evaluation evidence](https://github.com/SFHAJJI/lex/blob/main/docs/assistant-evaluation-scenario-matrix.md) | EUR-Lex intervals are official consolidated-wording states, not an independent conclusion about entry into force or applicability. |
| User-interface citation | **Built; Measured.** `UiMapper.From` maps only typed fields into a provision view and preserves `/eu-eurlex/32016r0679/2016-05-04--af3e8edcc8aeb9b8c10e891880377cb0b363a8fa7005a1b45557d21afa592de5#art_6` as the citation target. | [`UiMapper.From`](https://github.com/SFHAJJI/lex/blob/main/src/Lex.Ask/UiMapper.cs); [`AgentEvidenceLedgerTests`](https://github.com/SFHAJJI/lex/blob/main/tests/Lex.Tests/AgentEvidenceLedgerTests.cs) and [`assistant-shell.test.ts`](https://github.com/SFHAJJI/lex/blob/main/web/src/assistant-shell.test.ts) | A user-interface (UI) citation proves the result's source coordinate and hashes; it does not turn model prose into legal authority or advice. |

## Assistant evaluation gate

| Bound fact | Current release contract |
|---|---|
| Catalog author | `Lex release engineering`, identified as `system:lex-release-engineering` |
| Evaluation reviewer | Soufien Hajji, using a separate non-exportable evaluation-review signing key |
| Review claim | The project owner reviews the catalog produced by the release-engineering identity. This is separation from the catalog author, not third-party review, external audit or legal review. |
| Frozen set | 25 frozen scenarios, 48 final candidate HTTP requests, 8 same-thread setup HTTP requests, 56 total candidate HTTP requests and 48 release-grader requests |
| Candidate token budget | The current reservation is 928,000 input and 92,000 output tokens. Setup and final turns are charged to this candidate budget. |
| Grader token budget | The current reservation is 815,104 input and 384,000 output tokens on a separately authenticated `gpt-5-nano` deployment. |
| Cost control | The catalog has an outer EUR 10 ceiling, where `EUR` is the standard currency code for euros. The current maximum-token reservation prices at EUR 0.5356487 before inference, then measured use is gated again. This is not a live Azure billing cutoff and cannot interrupt an in-flight model call. Signed call counts and per-call token ceilings bound the run instead. |
| CI/CD | GitHub Actions creates an immutable zero-traffic candidate, verifies the signed artifact manifest and both jurisdictions, runs HTTP and browser evaluation, publishes signed evidence, then a separate protected workflow revalidates it before promotion or exact rollback. |

### Evaluation flow

Server-Sent Events (SSE) carry the versioned intermediate contract; the equal terminal object is
the authoritative completed response.

`Question -> resolve identity -> Plan once -> validate or correct once -> freeze -> execute typed tools -> typed SSE and terminal result -> optional runtime prose check -> deterministic release checks -> separate release grade -> signed promotion gate`

> **Why this design?** One bounded correction recovers a malformed plan before it can touch legal
> tools, while the freeze prevents a model from adapting its legal request after seeing results.
> This keeps planning useful without creating an observation-driven agent loop.
>
> ```text
> plan = validate(first_proposal) ?? validate(one_corrected_proposal)
> freeze(plan); execute_in_user_order(plan) // no observation, no replan
> ```

The Runtime grounding judge checks only optional generated factual prose during an ordinary request
and falls back to the typed result. The separate Release grader receives the frozen question,
rubric, bounded reply, typed operations and trace. Before grading, the deterministic runner checks
tool arguments, legal and transport outcomes, UI effect, model identity, tokens, latency and stream
equality. It reads typed SSE plus the matching terminal object—not Azure logs or model
chain-of-thought—and the signed report omits prompts, legal text, raw tool payloads and free-form
grader reasons.

## Verified limits

These are present release boundaries, not generic risks. A limit stays visible until the named
evidence exists; a model answer cannot waive it.

| Limitation | User impact | Current containment | Next evidence needed |
|---|---|---|---|
| Publisher and jurisdiction coverage is limited to EUR-Lex/EU and Legilux/Luxembourg. | Lex cannot establish coverage or absence for another publisher or jurisdiction. | Coverage responses disclose the mounted set and known gaps; required publishers are release-gated. | A new immutable source adapter, corpus/articles/index evidence and the same retrieval and release gates for that publisher. |
| Luxembourg hybrid retrieval remains quarantined because the accepted relevance and latency gate has not passed. | Luxembourg search uses deterministic keyword retrieval; conceptual recall may be lower. | Keyword is the production default and hybrid cannot activate from configuration alone. | A fresh signed holdout report meeting relevance, warm-latency, memory and regression thresholds. |
| EU hybrid retrieval is currently unproven for this release. | EU search also remains keyword-first; no semantic improvement is claimed. | The signed activation flag stays false unless the exact EU artifacts and model pass the frozen benchmark. | An accepted exact-release EU benchmark report, including holdout and cold/warm operational measurements. |
| Current image-only EU annex wording is not derived through optical character recognition (OCR) or searchable. | A reader may see the official record and source but cannot search or quote annex text Lex does not safely hold. | Source identity, source address and byte evidence are retained; the text state remains explicitly unavailable rather than guessed. | An additive, reviewed acquisition/extraction profile with fidelity measurements, protected corpus/articles commits and a rebuilt signed artifact release. |
| The exact 25-case assistant catalog is owner-reviewed and signed, but has not yet been executed against a candidate. | No claim is made yet that a candidate passed the new LU, language, empty, refusal, compound and continuation cases. | Preflight verifies the digest-bound owner review and trusted signature before inference; any catalog-byte change invalidates approval. | Live signed report, browser evidence and promotion verification for the same candidate identities. |
| Runtime quotas, idempotency and assistant thread state are process-local. | The service cannot safely scale to multiple active replicas today. | Release verification pins production, rollback and candidate revisions to one replica each. | Externalized shared state plus concurrency, failover and latency evidence before a multi-replica release. |

Operational thresholds are detailed under [limits and scale](/built/limits), retrieval activation
under
[local hybrid retrieval](https://github.com/SFHAJJI/lex/blob/main/docs/hybrid-search.md), dated gaps under
[known defects](https://github.com/SFHAJJI/lex/blob/main/docs/known-defects.md), and permanent guards
derived from failures under [incidents](/built/incidents).

## Container registry and Blob retention

The Azure Container Registry is shared and still has a legacy role-based access control (RBAC)
boundary. Broad digest deletion is rejected because Lex cannot prove that another application does
not pull a digest. The clean target is a dedicated registry or an audited move of every shared
consumer to repository-scoped attribute-based access control (ABAC) and managed identities; only
then can an exact-digest allowlist become an automated deletion plan.

Private Azure Blob staging objects have a narrower ownership proof. They are disposable
coordination records, not the canonical release. The publisher may delete only the exact
create-only staging names it created, with matching ETags, after the GitHub Immutable Release
assets and signature have been downloaded and matched by hash and length, or after a failed draft
has been reconciled. Canonical GitHub release tags and assets are outside Blob cleanup entirely.
Moving the canonical boundary to Azure immutable write-once, read-many (WORM) storage is a future
option only when retention or compliance requirements justify a separate architecture decision
and migration evidence.
