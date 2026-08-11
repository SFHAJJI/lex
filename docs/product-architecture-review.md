# Lex product architecture review

Status: accepted with the executable amendments in this document

Date: 2026-08-10

Maintainer decision: implement in reviewable vertical slices. Normal public MCP use remains
compatible, but newly enforced safety bounds are an intentional breaking contract and require the
single advertised MCP version to move to 2.0.0 with a migration note. The assistant and browser
stream may change when the versioned client and server ship together.

Scope: the public product, every public route, acquisition and ingestion, corpus and index
artifacts, retrieval, MCP, the assistant, browser behavior, accessibility, operations,
evaluation, security, and public engineering evidence.

The disposition of every external P0/P1 review finding is recorded in
`docs/product-review-reconciliation.md`.

This document is both an audit of the current production system and the accepted specification
for the next product increment. Implementation is authorized in the ordered tasks, but production
promotion remains conditional on every release gate.

## 1. Executive verdict

Lex already demonstrates senior engineering and architectural judgment. Its strongest
properties are not visual polish or model novelty. They are the deliberate separation of
publisher evidence, deterministic legal operations, retrieval, model-generated prose, and
presentation; the signed artifact chain; the public decision register; the measured rejection
of an underperforming semantic candidate; and the explicit representation of unavailable
evidence.

The product is not yet ready to be used as unqualified evidence of a finished AI architecture.
The legal data and retrieval core is strong, but the assistant can currently contradict the
authoritative operation result, refuse after a successful aggregate operation, and take minutes
to narrate results already available deterministically. The public site also hides much of its
best engineering evidence, duplicates the developer surface, and exposes one invalid dialog
semantic. These defects are concentrated enough to fix without replacing the platform.

The target is not a chatbot that keeps searching until it finds plausible text. Lex is a legal
operation controller:

1. The user expresses a research intent.
2. Application policy resolves only the identity needed by that intent.
3. MCP performs the authoritative operation.
4. A typed result becomes the normal workspace state.
5. Deterministic copy explains exact, aggregate, navigation, and gap results.
6. A model composes prose only when the user requested a synthesis across valid evidence.
7. A judge evaluates only model-generated factual prose.
8. No later evidence can turn a failed requested operation into a successful claim.

This model is simpler, faster, more truthful, and easier to explain than the current universal
model-finalization path.

## 2. Product promise and scope

### 2.1 Promise

Lex answers what official legal text was available for a work, article, or date; shows how a
held text changed when versions are comparable; ranks held works over a specified period; and
returns verifiable official evidence. It refuses or discloses a gap when the held evidence cannot
support the requested operation.

Lex does not decide compliance, applicability to a user's facts, or legal meaning. Public copy
must describe exact retrieval, comparison, and bounded grounded synthesis without claiming all
Luxembourg law, all EU law, or legal advice.

### 2.2 Audiences

- Legal and compliance professionals need fast, date-aware, source-linked research.
- Engineers need stable MCP contracts, explicit filters, and verifiable artifacts.
- Technical evaluators need concise evidence of architecture choices, tests, benchmarks,
  failures, operational controls, and tradeoffs.

The interface should lead with the legal task. Engineering evidence should be one predictable
navigation action away, not mixed into the research workflow and not buried in the footer.

## 3. Current-state audit

| Area | Current verdict | Keep | Change |
|---|---|---|---|
| Product scope | Honest on dedicated pages, but easy to overstate conversationally | Publisher and corpus-specific coverage | Put scope and known exclusions next to benchmark and assistant claims |
| Acquisition | Strong official-source discipline with bounded reads and immutable extraction profiles | Official endpoints, pacing, source-specific adapters | Document retry and metadata-only failure policy as an operational contract |
| Corpus | Strong append-only evidence and derived-content separation | Raw hashes, extraction identity, article boundaries, gap records | No structural redesign |
| Artifact integrity | Excellent and distinctive | Signed manifests binding index, vectors, model, tokenizer, scope, and build identity | Keep verification visible and automate freshness disclosure |
| Index | Appropriate local, filter-first SQLite design | FTS5, temporal tables, bounded candidates, portable signed bytes | Establish formatter baseline; change internals only for proven correctness or bounds |
| Keyword retrieval | Production-safe with excellent exact identity behavior and low latency | Keyword as production default | Improve conceptual recall through evaluation, not unmeasured tuning |
| Hybrid retrieval | Correctly quarantined after failing the public gate | Pinned model, reproducible benchmark, fail-closed activation | Expand Luxembourg holdout and investigate latency before another candidate |
| MCP | Strong transport-neutral deterministic tool core | Typed operations and shared in-process core | Make authoritative operation status explicit enough for answer policy to consume |
| Assistant resolution | Strong guarded work identity and explicit clarification | Raw-user authority, bounded tab history, typed effects | Resolve lazily for work-specific operations instead of raw-searching every request |
| Assistant answers | Release blocker | Typed effects, evidence ledger, application-owned gaps | Add authoritative outcome precedence and a deterministic answer policy |
| Main workspace | Clear, restrained, and fast | Editorial legal visual language, normal workspace rendering | Prevent the launcher from covering content and clarify assistant/workspace ownership |
| Catalogue | Honest and responsive | Flat source-class facets, human labels plus publisher codes, text coverage | Do not invent a hierarchy between legal forms; improve short explanatory copy only |
| Engineering pages | Substantively excellent | Built, decisions, architecture, verification, coverage, benchmarks, stories | Make them discoverable under one accessible `Check the work` navigation item |
| Developer pages | Duplicated | `/developers` as the source of truth | Redirect or reduce `/ai`; do not maintain the same setup twice |
| Accessibility | Strong on ordinary routes | Keyboard-visible controls, good contrast, responsive catalogue | Correct assistant landmark/dialog semantics and test docked and modal modes |
| Performance | Excellent ordinary-page performance | Server-rendered shell, small client bundle, local index | Remove model calls from deterministic assistant operations; add assistant latency budgets |
| Security | Good baseline | Managed identity, verified inputs, security headers, bounded input | Fix global quota accounting; consider CSP separately after inline assets are reduced |
| Operations | Strong signed promotion and candidate smokes | Fail-closed mount, rollback revision, publisher-specific verification | Replace reply-exists assistant smoke with semantic contract smokes |
| Documentation | Rich and unusually candid | Decision register, rejected alternatives, measurements, failures | Reconcile claims with current assistant behavior and current automation state |
| Maintainability | Good boundaries, several oversized composition files | Architecture fitness tests | Extract policy only where current defects prove a missing boundary; avoid broad cleanup |

## 4. Measured baseline

The review baseline is current `main` at `7239f50` and the production site on
2026-08-10.

- .NET: 467 tests passed; full build passed with no compiler warning or error.
- Web: 38 tests passed; production build passed; npm audit reported no vulnerability.
- Public routes sampled across research, catalogue, coverage, decisions, architecture,
  verification, benchmarks, developer, and assistant surfaces returned successfully.
- Ordinary routes achieved perfect accessibility, best-practice, and SEO scores in sampled
  Lighthouse audits. The assistant workspace exposed an invalid `aside` plus `dialog` semantic.
- The homepage measured roughly 267 ms LCP and 0.06 CLS in the sampled desktop trace.
- The current combined semantic activation gate is false. Keyword remains the correct default.
- The repository is not `dotnet format` clean: 23 of 126 C# files have formatting findings.

These measurements are evidence for prioritization, not permanent performance claims. Release
evidence must record the commit, index manifest, benchmark identity, resource envelope, and date.

### 4.1 Automation and production state

At the review baseline, GitHub Actions is disabled for `lex`, `lex-ops`, and the two corpus
repositories because the private-account included minutes were exhausted. Production nevertheless
serves indexes built on 2026-08-09, so public copy that says `nightly` describes the previous
operating mode rather than the active scheduler.

The approved target is no paid GitHub Actions minutes, not a ban on every Actions execution:

- audit `lex-ops`, including reachable Git history, before making it public;
- use standard GitHub-hosted runners only in public repositories for short CI, verification,
  signing, publication, registry, heartbeat, and deployment jobs;
- keep larger runners and billable Actions artifact storage disabled;
- build the long fleet locally because a hosted job has a six-hour execution ceiling;
- publish locally built indexes through a short verified prebuilt workflow using the existing
  GitHub OIDC and Key Vault identities;
- keep the `$0` Actions budget as a final billing guard;
- render scheduler and freshness statements from mounted evidence rather than unconditional copy.

No new local Azure deployment identity is required. GitHub OIDC remains the production release
identity and the long-running computation happens before the short publication workflow begins.
Gate 0 uses the existing production Container App with a new `promote=false` deployment mode: build
and verify a zero-traffic candidate first, then promote and rollback through separate explicit
operations. No staging environment or staging identity is assumed.

## 5. Required assistant architecture

### 5.1 Closed operation plan

Before any non-supporting legal tool executes, the planning phase returns an ordered internal
`OperationPlan` containing request ID, locale, and one `RequestedOperation` per user-requested legal
operation. Each requested operation has an immutable operation ID, user-order index, result class,
primary tool or application action, normalized arguments, required work resolution, allowed
supporting call roles, and allowed effects. Application code validates the plan against the
tool/status table and work authority before execution. The plan is internal and does not add fields
to public MCP calls.

Execution creates one `OperationExecution` per requested operation in a non-terminal `pending`
state. It may transition exactly once to a terminal `OperationResult` containing the legal and
transport outcomes defined in section 5.2. Supporting calls update evidence owned by that
execution but cannot create, replace, reorder or complete another requested operation. Cancellation
or an execution deadline terminally completes every remaining pending operation with its own typed
result.

Resolution searches and outline calls are assigned as supporting calls to one requested operation.
The first operation in user order is primary only for reply ordering; every requested operation has
its own authoritative outcome and reply owner. Tool execution order cannot change those outcomes.
A search whose user intent is only to find or open a work becomes application-owned `navigate`.
Later calls may add typed views but cannot replace any frozen requested operation or overwrite a
terminal result.
When planning cannot produce an unambiguous ordered operation list, the application asks a bounded
clarification before executing legal state tools.

Result classes are `navigate`, `exact_text`, `comparison`, `timeline`, `ranking`, `inventory`, and
`verification`. Application dispositions are separate: `synthesis`, `clarification`, `gap`, and
`legal_boundary`. This separation prevents a clarification generated before a tool call from being
misclassified as an MCP result.

The mapping is total:

| Tool or action | Primary class | Notes |
|---|---|---|
| `search` | supporting or `navigate` | Supporting for identity/article resolution; primary only for find/open intent |
| `as_of` | `exact_text` | Outline used only to resolve an anchor is supporting |
| `diff` | `comparison` | `profiles_differ` is an authoritative comparison gap |
| `timeline` | `timeline` | Version history for one work |
| `article_history` | `timeline` | Provision history for one anchor |
| `changes_in_period` | `ranking` | Corpus population and publisher timeline semantics are mandatory |
| `in_force_on` | `inventory` | Never call EU consolidation state legal entry into force |
| `coverage` | `inventory` | Panel view; it need not become a URL-addressable research workspace |
| `cited_by` | `inventory` | Describes extracted textual references, not legal dependency or amendment proof |
| `provenance` | `verification` | Panel view with source and artifact evidence |
| resolved application coordinate | `navigate` | No MCP result is required after deterministic resolution |

### 5.2 Authoritative outcomes

Legal outcome is one of `not_evaluated`, `succeeded`, `succeeded_empty`, `needs_clarification`,
`not_available`, `not_comparable`, `not_found`, `invalid_request`, or `legal_boundary`. Transport
outcome is separate: `completed`, `cancelled`, `timed_out`, `upstream_failed`, or `over_quota`.
`not_evaluated` is valid only when transport prevents the requested legal operation from reaching
a legal result; it cannot support a legal claim and must render a transport-specific gap. Once a
legal result exists, optional composer or judge failure preserves that result rather than replacing
it with `not_evaluated`.

| MCP status or condition | Legal outcome |
|---|---|
| `ok`, successful navigation | `succeeded` |
| `no_result`, `no_changes_in_period` | `succeeded_empty` |
| unresolved or ambiguous required work/parameter | `needs_clarification` |
| `profiles_differ` | `not_comparable` |
| `unknown_work`, `unknown_anchor` | `not_found` |
| `no_version_for_date`, `anchor_not_in_version`, `no_provision_history`, `text_not_available`, `text_withheld` | `not_available` |
| `no_corpus_mounted` or a missing required publisher | `not_available` and readiness failure |
| validation exception or invalid tool arguments | `invalid_request` as a typed result, with no internal exception text |
| advice, compliance conclusion, application to user facts, recommendation, or evasion request | `legal_boundary` |
| cancellation, deadline, upstream failure or quota rejection before a legal result | `not_evaluated` with the matching non-completed transport outcome |

`needs_clarification` and failure outcomes dominate supporting evidence for claims about their
requested operation. A failed comparison may show two exact snapshots but may not claim a change.
In a compound request, failure of a secondary operation is rendered and narrated as its own gap;
success of another operation cannot hide it.
Status constants and this mapping have one implementation source shared by answer policy, UI
mapping, evidence collection, public tool documentation, and tests. `outside_observed_window` is
removed unless a producing path and fixture are implemented.

### 5.3 Answer and model policy

| Primary class or disposition | Reply owner | Workspace owner | Post-result model calls |
|---|---|---|---|
| Navigate | Localized application template | Typed navigation | 0 |
| Exact text | Localized application template | Provision/version view | 0 |
| Comparison | Localized application template | Diff or gap view | 0 |
| Timeline | Localized application template | Timeline/history view | 0 |
| Ranking or inventory | Localized application template | Ranking/list/panel view | 0 |
| Verification | Localized application template | Verification panel | 0 |
| Clarification, gap, legal boundary | Localized application contract | Typed view | 0 |
| Requested descriptive synthesis | Composer, then judge | Evidence views already displayed | 1 composer and at most 1 judge |

Application templates exist in English and French and are chosen by a deterministic request-locale
rule. The workspace owns long text, rows, comparison markup, proof, pagination, and sources. Chat
states the operation, population/date boundaries, material gaps, and a concise next action.

Composition is limited to descriptive synthesis over accepted evidence. It cannot decide
compliance, applicability to user facts, legal meaning, or a recommended course of action. A
composer contract failure gets one bounded contract retry. A judge repair is contract-validated;
it is not accepted unchecked. Any composer, judge, cancellation, or upstream failure preserves
the completed typed workspace and returns a deterministic evidence-limited reply with HTTP 200.

### 5.4 Resolution and work authority

Work-independent aggregates run without a work search. Before a work-specific tool executes, the
guard must hold authority for every requested work. With no observed authority it denies.

Authority may come only from:

1. deterministic exact or strong resolution of the current raw user text;
2. direct anchored provision evidence from a bounded search, including a model reformulation used
   for problem-first discovery;
3. an atomic clarification value selected by the user;
4. authority carried from the most recent resolving user turn in the preceding three user turns.

Strong evidence has a non-empty legal anchor and a `keyword`, `fuzzy`, or provision-level
`semantic` reason. `work_metadata`, `semantic_concept`, bare `article_intent`, and anchorless hits
are weak. Weak evidence can propose bounded clarification choices but never authorize a work.

Carried authority uses the most recent resolving turn only, includes all works resolved in that
turn, is replaced by a newer resolving turn, and is cleared by `New conversation`. It never
accumulates silently across topics. A clarification selection authorizes exactly its opaque work
value and replays the pending intent once. `None of these` exits without consuming model quota.
Unrelated text is a fresh question. At most one repeated clarification is allowed before a bounded
gap response.

### 5.5 Conversation and reset

Conversation is tab-scoped and server-stateless. The browser sends a bounded transcript to the
server and Azure OpenAI when model planning or synthesis is needed, so copy must not claim that the
content remains only in the browser. The UI warns against submitting confidential client facts and
links to the data-handling explanation.

`New conversation` clears transcript, pending clarification, carried work authority, and model
context, but retains the current legal workspace. The accessible name is `New conversation`.
Current-user messages over the server limit are rejected visibly rather than silently truncated;
stored assistant history may be bounded without altering the current request.

### 5.6 Streaming and idempotency

The assistant stream is versioned and emits:

1. bounded `step` events containing only observable operations;
2. one `operation_result` for each requested operation as soon as its typed result is accepted;
3. optional synthesis status without chain-of-thought;
4. one terminal `done` or typed transport error.

Every event carries request ID and monotonically increasing sequence. The browser ignores stale or
cancelled request IDs. A completed operation result stays visible when later composition fails.
The browser does not retry a failed streaming POST as a second non-idempotent request. A retry must
reuse the same idempotency key and the server must not execute model work twice.

### 5.7 Evidence and temporal semantics

Every legal typed effect carries a bounded evidence context: publisher, jurisdiction, timeline
semantics, requested date/window, observed and valid coordinates, provisional state, source URI,
extraction profile where relevant, content hashes, artifact manifest identity, and verification
status. Detailed proof may be collapsed in the UI but cannot disappear during MCP-to-workspace
mapping.

Future-dated evidence is labelled provisional in the short reply and workspace. EUR-Lex official
consolidation dates are never described as entry into force. `changes_in_period` counts publisher
version dates and its reply names that axis. `cited_by` reports extracted publisher references; it
does not assert amendment causality or legal dependency.

### 5.8 Quota and failure isolation

The first release pins the public assistant to one application replica. This is deliberately
simpler than adding a distributed counter service for a portfolio-scale system. Per-client quota,
the busy gate, and then global accepted-request accounting run in that order under one atomic
reservation. Rejected and duplicate requests consume no accepted-request allowance.

The application counter is best-effort abuse and cost friction, not a durable billing ledger.
Azure model quota and the release evaluation token envelope are the hard external controls. Public
MCP remains separately bounded by input, output, candidate, concurrency, and rate controls; it is
not described as unlimited.

## 6. Required information architecture and interaction design

### 6.1 Header

Preserve the existing restrained editorial design. Replace scattered engineering navigation with:

- `Lex`
- `Browse everything`
- `Check the work`
- `For developers`

`Check the work` is an accessible disclosure menu containing:

- How it works
- Coverage
- Architecture
- Decisions
- Benchmarks
- Verify the artifacts
- How I built it
- About

It opens on pointer hover only as an enhancement. Click and keyboard focus are first-class; Escape
closes it; focus returns to the trigger; mobile uses a disclosure list rather than hover behavior.
Header and footer links may repeat for conventional wayfinding, but their labels and hierarchy must
make the repetition intentional.

### 6.2 Homepage

Keep the search-first entry and three task-oriented paths. Replace the biography-oriented third
path with an engineering evidence path:

`I want to inspect the engineering` -> architecture, decisions, benchmarks, failures, and proof.

The page should make three facts understandable without scrolling through a technical essay:

1. Lex answers date-aware questions from official held text.
2. Every result exposes its source and verification state.
3. Retrieval and assistant behavior are measured and fail closed.

Do not turn the homepage into an AI portfolio landing page. The product earns credibility by being
useful first and inspectable second.

### 6.3 Catalogue

Keep jurisdiction as the top-level partition and source class as a flat publisher-specific facet.
`REG`, `REG_DEL`, `RGD`, `CODE_RECUEIL`, and similar values are legal or publisher document classes,
not a universal parent-child taxonomy. A fabricated tree would misrepresent the data.

Keep `full text`, `partial`, `collection metadata`, and `record only` visible. Explain `record only`
once near the facet/results header: Lex holds identity and timeline metadata but not searchable
provision text for that record. Do not hide gaps to make coverage appear larger.

### 6.4 Developer surfaces

`/developers` owns MCP setup, tool contracts, datasets, and the live playground. `/ai` must either:

- redirect permanently to the relevant `/developers` anchor; or
- contain a short AI-specific orientation that links to the single canonical setup.

It must not duplicate the developer page implementation or content.

### 6.5 Assistant shell

- Closed by default on first visit; preserve tab-scoped open/minimized state.
- At 320 by 568 pixels, at the top and bottom of every tested route, the launcher must not
  intersect a visible link, button, input, pagination control, chip, or the final content line.
  Reserve bottom clearance of at least launcher height plus 12 pixels. Transient overlap of
  non-interactive text while mid-scroll is acceptable.
- At 1,100 pixels and wider, desktop uses a complementary dock that reflows the workspace. At
  1,099 pixels and narrower, use a real modal dialog with backdrop, background inertness, scroll
  lock, focus containment, Escape, close, and focus restoration.
- Do not put `role=dialog` on an `aside`.
- Starter prompts are executable acceptance examples and must remain correct in production.
- During work, show the operation being performed without exposing chain-of-thought.
- Exact, comparison, timeline, ranking, inventory, navigation, and search results update the same
  URL-addressable workspace state as direct controls. Coverage, provenance, cited-by evidence,
  clarification, and gaps are deliberate panel views. Planner pagination is represented in the
  workspace URL; it is never silently discarded.

`Check the work` is a disclosure button plus a navigation list, not an ARIA application menu. It
opens on click and keyboard activation. Hover may open it only on hover-capable pointers, with a
short close delay that permits movement into the panel. `aria-expanded`, Escape, outside click,
focus return, mobile touch, forced colors, reduced motion, 200 and 400 percent zoom, mobile
landscape, and virtual-keyboard behavior are acceptance cases.

### 6.6 Route ownership

`docs/public-route-ledger.md` is the source-controlled inventory for every public endpoint. Each
entry identifies method, pattern, owner, audience, canonical or redirect behavior, content type,
indexability, security policy, and representative success/failure fixture. A test enumerates
ASP.NET `EndpointDataSource` and fails when a mapped route is absent from the ledger.

`/developers` owns MCP setup and the live playground. `/ai` becomes a permanent redirect to the
assistant section of `/developers`. `/about` remains reachable from `Check the work` and the
footer. `/stories`, `/find`, and `/changed` remain first-class routes. The no-JavaScript catalogue
surface is explicitly preserved.

## 7. Retrieval and ingestion decisions

### 7.1 Preserve the ingestion architecture

No ingestion redesign is justified by this review. Keep official-source adapters, immutable raw
evidence, versioned extraction profiles, deterministic derivation, signed build inventory, and
fail-closed mount verification. They form the strongest architecture narrative in the product.

Add only the following operational contract, enforced by adapter tests and promotion verification:

- HTTP 408, 429, 500, 502, 503, 504 and bounded network timeouts retry with capped exponential
  backoff, publisher `Retry-After`, and a maximum attempt count recorded in the build report;
- permanent 404/410, publisher-declared metadata-only records, oversized bodies, parser failure,
  and retry exhaustion become distinct typed build issues rather than silent `null` returns;
- incomplete enumeration never creates tombstones and never publishes as a complete inventory;
- production declares a required publisher set and expected work inventory. `/readyz` fails and
  promotion remains at zero traffic if any required verified index is missing or incomplete;
- deliberately scoped builds declare their smaller required set and every aggregate discloses it;
- a clean previous index and corpus release remain selected until the candidate passes; rollback
  restores the previous image and exact previous manifest set.

### 7.2 Preserve keyword as production default

Do not activate hybrid retrieval to improve appearances. Current evidence rejects it on relevance
and latency. The next semantic candidate requires:

- at least 25 Luxembourg holdout cases, including at least 5 conceptual cases and at least one
  negative, temporal, role-intent, ambiguity, exact-name, and comparison case;
- no normalized query overlap between tuning and holdout. Work overlap is allowed because the
  product retrieves recurring laws, but the report records the overlap and includes an explicitly
  reviewed tuning-unseen-work subset;
- exact-name, ambiguity, negative, conceptual, comparison, and temporal cases;
- work-level ranking metrics;
- warm holdout p95 below 250 ms, measured memory below 75 percent of the configured limit,
  conceptual nDCG improvement of at least 10 percent, and no gated metric regression beyond
  2 percent;
- signed case, model, tokenizer, code, and artifact identities;
- a public pass before default activation.

Model-derived discovery metadata remains weak, quarantined, and unable to resolve identity or
support legal claims.

Every gated denominator must be non-empty for every frozen collection. Empty denominators are
`not_measured`, never 1.0. Any threshold change requires an accepted architecture decision.

## 8. Evaluation and release contract

### 8.1 Deterministic assistant contract suite

Tests must cover route, authorized work, MCP tool and arguments, authoritative outcome, typed UI
effect, evidence context, reply owner, locale, model-call count, and forbidden claims. Each case
states whether it runs against the extended fixture, a recorded MCP contract, or production. The
fixture adds a second publisher, same-title ambiguity, reviewed aliases, an extraction-profile
pair, a future-dated work, metadata-only records, and direct/weak discovery hits. Minimum cases:

- show Article 6 GDPR as of 2021;
- compare Article 92 CRR between 2020 and 2024 when profiles match;
- the same comparison when profiles differ;
- rank Luxembourg and EU laws changed most in 2024;
- show when Article 92 of CRR changed;
- ambiguous official title;
- unknown work identifier;
- weak discovery only;
- problem-first discovery with direct anchored evidence;
- exact professional alias;
- multi-work follow-up date or article using carried authority;
- valued clarification, `None of these`, and unrelated follow-up;
- provisional future-dated evidence;
- French exact result and French comparison gap;
- advice, compliance, personal-fact application, recommendation, role-play, and evasion requests;
- missing required publisher and deliberately scoped publisher population;
- exhausted per-client and global model quotas.

Every production starter prompt is copied into this suite and names its expected primary tool,
outcome, typed effect, and population. A fixture gap is not deferred to production.

### 8.2 Browser acceptance suite

Use Playwright with Chromium against a fixture container and axe-core. Run at 1,280 pixels,
1,100 pixels, 1,099 pixels, 320 by 568 pixels, mobile landscape, 200 and 400 percent zoom, forced
colors, and reduced motion. Verify:

- header disclosure by pointer, keyboard, and touch;
- every route and internal navigation target in `docs/public-route-ledger.md`;
- assistant open, minimize, close, reset, focus containment, and restoration;
- launcher collision against homepage chips, legal text, catalogue pagination, and footer;
- exact text, diff, ranking, timeline, inventory, verification, clarification, and gap effects;
- back/forward URL restoration;
- current-request length rejection and stale-response suppression;
- light and dark modes;
- no serious or critical axe violation, console error, failed request, horizontal overflow, or
  malformed accessibility tree.

Run the same named semantic smoke classes against the zero-traffic production candidate and one
post-promotion route smoke. Commit a dated report with browser version, viewport, route set,
candidate revision, code commit, and manifest set.

### 8.3 Live assistant evaluation

Keep a small deterministic mock suite in every local release check. Run a frozen live-model
evaluation intentionally, not on every documentation commit. Release mode fails when a case is
missing, ungradeable, empty-context, stale, self-inconsistent, below threshold, or when its grader
is unavailable. Keyword fallback cannot pass an LLM-only rubric. The frozen clock is part of the
case identity.

The v3 evaluation is blind-authored and independently reviewed before expected outcomes are
unsealed. Its report records case and prompt digests, evaluator and grader deployments, grading
mode per case, repetitions, code commit, artifact manifests, resource and memory limits, token
usage, pass thresholds, failures, and latency percentiles. The report and signature use the
`keyvault-lex-v2` trusted root, are verified on `/verify`, and must match the promoted commit and
manifest set. Existing v2 reports remain public historical evidence.

The release candidate smoke must assert semantic outcomes for representative exact, aggregate,
comparison-gap, and clarification cases. Checking only that a JSON `reply` exists is forbidden.

One live release run is limited to 20 frozen cases, 1,000,000 input tokens, 100,000 output tokens,
and an estimated cost of EUR 10 at the then-current deployment price. The runner calculates the
estimate before inference and aborts if any envelope would be exceeded. Production audit calls use
a separately reserved quota and do not consume the public allowance.

### 8.4 Performance budgets

- Lighthouse mobile budgets for the committed representative route set are LCP at most 2.5 s,
  CLS at most 0.10, and TBT at most 200 ms under the committed configuration.
- Deterministic assistant operations require no composer or judge call and at most two bounded
  planning rounds, including one repair. Each planning call has a 12 s deadline. Submit-to-first
  `operation_result` has a 15 s production p95 and a 25 s hard request deadline.
- After MCP returns an accepted result, the browser displays its `operation_result` within 500 ms
  locally and 1.5 s production p95.
- Model synthesis latency is reported separately and cannot block display of a completed workspace
  operation. Its production p95 envelope is 45 s.
- Candidate and memory bounds remain independent of corpus size for a fixed query limit.
- Retrieval activation retains the numeric limits in section 7.2.

Budgets, route selection, Lighthouse version, throttling profile, invocation, and raw results live
in a committed budget artifact. A decision-register entry is required to weaken a threshold.
The signed assistant report consumes the versioned event stream and records submit-to-first-result,
terminal, planner, MCP, optional-synthesis, and transport/queue-residual durations separately. The
residual is the client-observed first-result duration minus the server's monotonic emission duration;
it is deliberately not labelled network latency. Browser presentation is measured by the exact-code
Playwright gate as `operation_result` received-to-presented after the next animation frame. Five
independent operations run without route mocks against the exact zero-traffic revision FQDN. An HTTP
runner cannot truthfully claim compositor paint, so the browser evidence is a required file in the
same signed code/revision release rather than a fabricated field in the assistant report.

## 9. Security and operational consistency

- Preserve managed identity, input bounds, official-host allowlists, verified artifacts, and
  restrictive browser headers.
- Enforce the 64 KiB assistant-body limit while streaming bytes, independent of `Content-Length`.
  Bound every MCP string, integer, limit, offset, response collection, and concurrent request before
  allocation or multiplication. Invalid parameters return bounded typed 4xx or JSON-RPC errors.
- MCP 2.0 admits at most 8 executing calls and 16 queued calls, with a 2 s queue deadline. It admits
  at most 120 calls per trusted client and 600 calls globally per rolling minute. Excess work fails
  before tool execution with bounded JSON-RPC server errors `busy` or `rate_limited`. Hybrid search
  has a nested concurrency ceiling of 2. A committed burst and sustained-rate test proves recovery,
  bounded memory, and stable latency.
- Accept the client address only from the trusted ingress hop. Document per-client limits as
  best-effort friction because NAT can share an address and IPv6 clients can rotate addresses.
- Fix quota ordering and pin one application replica before public promotion.
- Treat user text, transcript, publisher text, metadata, and tool output as untrusted instructions.
  Release tests place prompt-injection canaries in each channel and assert no unauthorized work,
  tool, URL, policy transition, or log/trace disclosure.
- Document browser storage, Azure processing, IP-derived counters, logging allowlists, redaction,
  retention, telemetry, and confidential-input guidance. Raw user text and IP addresses are not
  written to application logs, traces, metrics, or error bodies.
- Treat a strict Content Security Policy as a separate hardening increment because current inline
  assets require migration; do not claim it until enforced.
- `/healthz` remains process liveness. `/readyz` verifies the exact required publisher set,
  signatures, inventory completeness, and configured manifest set.
- Public freshness and nightly automation claims must match the actual scheduler state.
- Only standard hosted jobs in audited public repositories may run while the no-paid-Actions
  constraint is active. Long fleet computation remains local. Publication and deployment use the
  existing OIDC environments and `keyvault-lex-v2` signing root.
- `LEX_REQUIRE_ARTIFACT_MANIFEST` is enabled in production and ultimately retired after the
  compatibility window. Stamps are trusted only through a configured root, never merely through a
  public key embedded in the same stamp. Key rotation and compromise response are documented.
- HTTP and stdio mounts share one verification routine; any intentionally weaker local mode is
  explicit and cannot be confused with production.

## 10. Maintainability boundaries

Do not split files merely to appear architectural. Extract policy components only where the
current defects show a real missing boundary:

- `ResolutionPolicy`
- `OperationPlan`
- `AuthoritativeOutcome`
- `AnswerPolicy`
- `EvidenceContext`
- `AssistantQuota`

Keep deterministic rendering and legal operation contracts free of model SDK dependencies. Keep
MCP transport-neutral. Any public contract change must be versioned or migrated explicitly.
The assistant adapts MCP internally. MCP 2.0 preserves the normal `7239f50` HTTP and stdio tool
corpus but explicitly adds bounded parameters, bounded response behavior, sanitized validation
errors, and overload responses. Inputs accepted only because v1 lacked a maximum may now be
rejected; this is documented as the breaking migration. One `LexMcpVersion.Current` value feeds
HTTP initialize, stdio initialize and `server.json`, which must all report 2.0.0. `lex-index/2`
remains readable through the rollback window and receives a dated sunset decision before removal.

Establish a formatter baseline in a formatting-only commit or adopt a changed-lines gate. Never mix
repository-wide formatting with assistant behavior changes.

## 11. Delivery increments

Detailed dependencies, commands, file scope, and acceptance criteria live in
`tasks/product-architecture-plan.md` and `tasks/product-architecture.md`. The older
`tasks/plan.md` and `tasks/todo.md` are historical records of the completed publisher-first
retrieval programme and are not normative for this release. The release is intentionally split so
each pull request stays reviewable:

1. specification, route ledger, accepted architecture decision, and zero-cost release contract;
2. required-publisher readiness, signed prebuilt publication, candidate promotion, and rollback;
3. operation plan, status mapping, fail-closed work authority, and deterministic answer policy;
4. versioned streaming, idempotency, conversation context, quotas, public bounds, and privacy;
5. assistant dock/modal behavior, navigation, route ownership, catalogue copy, and accessibility;
6. ingestion failure contract, evaluation integrity, retrieval benchmark migration, and public
   evidence;
7. signed artifacts, production promotion, semantic and browser verification, rollback rehearsal,
   and consistency cleanup.

Every behavior slice begins with a failing regression. Golden changes are separate by page family,
require `LEX_GOLDEN_UPDATE=1`, assert non-trivial content, and fail CI when tests leave golden files
dirty or untracked.

## 12. Release gates

The increment is not complete until all are true:

- Every MCP tool, emitted status, validation path, primary class, and legal outcome has exactly one
  tested internal mapping.
- No successful-looking prose contradicts an authoritative gap, failed comparison, missing
  publisher, provisional state, or legal-boundary disposition.
- A successful ranking never asks for an unrelated instrument and always discloses publisher set,
  date window, order, and version-date semantics.
- Every production starter prompt produces its specified tool, outcome, workspace, and localized
  concise reply.
- Deterministic operations do not invoke composer or judge models.
- A composer or judge failure retains the accepted typed result and returns a bounded fallback.
- Work-specific tools deny before authority; weak discovery never authorizes them; clarification
  and carried authority authorize only their exact work set.
- The public assistant quota cannot be exhausted by rejected, busy, duplicate, or overlong
  requests from one client, and production is pinned to one replica.
- Oversized chunked requests and extreme MCP parameters fail with bounded errors before allocation
  or model execution.
- MCP burst and sustained-rate tests prove the execution, queue, hybrid and rolling-rate ceilings,
  reject before tool execution, recover after overload, and stay inside memory/latency budgets.
- `/readyz` fails when either required production publisher or its verified complete inventory is
  missing. Scoped deployments disclose their smaller population.
- Desktop, modal, zoom, forced-color, mobile, keyboard, touch, and launcher-collision cases pass
  the committed Playwright and axe suite.
- Every mapped public route is present in the route ledger, has one owner, and passes its declared
  canonical, content, security, success, and failure checks.
- .NET tests, web tests, build, security audit, format policy, browser matrix, and artifact
  verification pass.
- Retrieval remains keyword by default unless a new signed v3 report passes every numeric gate
  with non-empty per-collection denominators.
- The live assistant report fails closed, is signed by `keyvault-lex-v2`, matches the candidate
  commit and manifest set, and stays within the approved token and cost envelope.
- Public measurements identify date, commit, artifact, model, resource envelope, scope, and
  scheduler state. Public refusal and status lists match the shared emitted constants.
- The candidate is verified at zero traffic, promoted with exact identities recorded, and the
  previous revision plus manifest set is restored successfully in one rehearsed rollback.
- Every actionable Copilot and adversarial review comment is resolved or explicitly rejected with
  evidence before merge.

## 13. Explicit non-goals

- Replacing SQLite because a cloud search service sounds more architectural.
- Activating semantic retrieval despite a failed gate.
- Adding a multi-agent hierarchy where one bounded operation controller is sufficient.
- Inventing a hierarchy across publisher document classes.
- Hiding metadata-only records or known scope exclusions.
- Refactoring every large file during a correctness release.
- Optimizing the product as an interview demonstration at the expense of legal-user clarity.

## 14. Decision requested

The maintainer accepted this amended architecture on 2026-08-10 and authorized implementation,
public publication of `lex-ops` after the secret audit, and production promotion after every gate
above passes. Approval does not authorize paid GitHub Actions minutes, a new paid platform,
unbounded model inference, destructive cleanup of unknown worktrees, or bypassing signed artifact
and rollback gates.
