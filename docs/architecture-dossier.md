# Lex: the architecture dossier

Every number in this document was measured against the running system or read from a signed
artifact, never estimated. Where something is broken or unbuilt, it says so and links the record.
This is the complete, build-pinned engineering account rendered by the public dossier page.

Reading paths: §0 alone is a sixty-second summary. §1-§4 are the ten-minute story. Everything
else is reference depth, written so that one person could rebuild the system from it. A reader
arriving from the RAG literature can start at §13 instead: the four-brick mapping, and the
retrieval failure mode that frame lacks.

---

## 0. Sixty seconds

**What it is.** Point-in-time Luxembourg and EU law. Ask what an article said on a date and get
the publisher's wording back, with its identifier, effective date, and a hash chained to the file
the publisher served.

| five numbers | |
|---|---|
| Corpus | 2,650 works, ~7,010 dated versions, two publishers |
| Text held | EU 4,732 of 4,732 expressions; LU 3,157 of 4,649 (the gap is upstream, §3.3) |
| Retrieval | keyword nDCG@10 0.656 vs hybrid 0.560 on holdout, so keyword is the default |
| Under load | 255 requests, 0 failed, p50 147 ms, p95 456 ms, one replica |
| Memory | peak 498 MB against a 1.5 GiB budget |

**Three decisions.** Semantic search is built, measured and not the default. Extraction profiles
are immutable; improvements ship as new profiles. The deploy pipeline cannot move traffic;
promotion requires signed evidence it cannot produce for itself.

**One incident.** A question quoting the CRR by its full title was answered with the right
article of the wrong regulation. Retrieval identity, not the model. §8.3.

**One limit.** 6,424 of 537,035 provisions extract to empty text (1.2%), measured 2026-08-13,
worst cases named and one already recovered. §11.

**One caveat.** This is a one-person project, and several decisions below (single replica, free
public runners, a local build machine in the release path) are the honest consequences of that.
They are written down as decisions with costs, not hidden.

---

## 1. The problem

Ask any legal site what a law says and you get today's text. Almost every question that matters
in practice is about a date: what applied when the contract was signed, when the fine was issued,
when the breach happened. Official publishers hold dated consolidated editions, but scattered
across formats (Akoma Ntoso XML, Formex XML, legacy XHTML, gazette PDF), with no article-level
access and no machine interface.

Lex turns that into one queryable, verifiable history, and never answers the question it must not
answer: it retrieves and compares what the law said, and refuses to advise what anyone should do.

## 2. Forces

Architecture is the resolution of forces. These are the ones that shaped every decision below:

1. **Legal text is evidence.** It must be served verbatim, attributable to the publisher, and
   provably unaltered. Nothing model-generated may enter the corpus (D57, D75).
2. **Authority is cited data, never our opinion** (spec §3.6). Every classification, date and
   title is publisher-asserted or carries a named reviewer.
3. **Point-in-time correctness is the product.** A wrong date is worse than no answer.
4. **One person operates it.** The nightly must commit nothing when unsure, every gate must fail
   closed, and cost ceilings are enforced by pipelines, not attention.
5. **Small hardware, deliberately.** 1 vCPU, 2 GiB, one always-on replica. The single process
   makes the public quota ledgers authoritative and retrieval latency predictable.

## 3. The domain model

### 3.1 The FRBR shape

```
work (a law: eu-eurlex:32013r0575)
 └─ dated consolidation (valid_from 2026-06-26, valid_to open)
     └─ language expression (fr, en)
         └─ manifestation (the publisher's file: XML, HTML, PDF)
```

A "version" in Lex is a dated consolidation. Directory keys are `valid_from` plus a same-day
collision suffix (`2025-07-28--02`, D41). Version intervals close when the next version begins.

### 3.2 Two time axes

`valid_from`/`valid_to` describe when the law applied (the publisher's assertion).
`observed_from` describes when Lex first saw the record (the audit assertion). Every change to a
version's metadata appends an event; nothing is rewritten (F12).

### 3.3 What may be empty, and why that is honest

Legilux announces consolidations before publishing any document for them, including future-dated
ones. Lex records the announcement with `text_available=false` and a typed reason. Proven against
the publisher's own SPARQL catalogue: the financial-sector law has ten announced consolidations
since 2024-12-28 with no manifestation in any format. The 32% LU text gap is upstream reality,
not missing ingestion, and adding more fetch formats was measured to recover exactly zero
versions.

### 3.4 Identity discipline

Publisher identifiers are stored, compared and returned, never parsed for meaning. Colloquial
names ("CRR", "MiFID II") exist in no publisher dataset, so they live in a reviewed alias file
(50 rows, each with a named review), and a quarantine lane exists for model-derived candidates
with code-enforced confidence, repeat-run and agreement gates. That lane is empty in production.

## 4. Views

### 4.1 Context

```
official publishers            consumers
Legilux · EUR-Lex/Cellar       web UI (itself an MCP client)
        │                      AI assistant (/api/ask)
        ▼                      external MCP clients (/mcp)
   THE PIPELINE ──► signed artifacts ──► ONE PROCESS on Container Apps
```

### 4.2 Containers (repositories and runtime)

| repo | holds | written by |
|---|---|---|
| `lex` | all code, spec, ADR registry, eval catalog, trust roots | owner via gated PRs |
| `lex-corpus-lu-legilux` | LU evidence layer: verbatim publisher files + dated metadata, append-only | nightly only |
| `lex-corpus-eu-eurlex` | EU evidence layer, same rules | nightly only |
| `lex-articles` | derived per-article layer (Markdown + JSON), reproducible from evidence | nightly only |
| `lex-ops` | fleet workflows, publish scripts, immutable status/ticket branch | owner + workflows |
| `lex-git-lu` | LU law as a chronological git history, generated | pipeline |

The evidence/derived split is the recovery property: the derived layer can be deleted and
regenerated from evidence at any time, losing nothing but compute.

### 4.3 Components, one process

```
IndexReader   SQLite + memory-mapped vectors, per publisher
    │
McpCore       the typed tool surface: 10 tools, 13 closed statuses
    │
    ├── web UI        calls /mcp like any other client
    ├── assistant     plans typed operations, never free text
    └── external      public /mcp
```

`McpCore`, retrieval benchmarks and ingest-time verification call `SearchKeyword`/`SearchHybrid`
in-process. There is no internal HTTP between components (C5); it is one process by design.

### 4.4 Sequence: a question

```
question ─► planner (LLM, offered 12 operation names, frozen list ≤8)
         ─► plan gate: names not offered are rejected; arguments type-checked
         ─► operations execute against McpCore, no replanning
         ─► evidence ledger: typed entries with per-provision hashes
         ─► default reply: deterministic, 0% model-authored
         ─► optional synthesis: composer ─► typed claims bound to evidence ids
                                 ─► judge: pass / repair / refuse
```

### 4.5 Sequence: a night

```
02:17 UTC ─► enumerate publishers ─► ingest (append-only, hash everything)
          ─► anomaly gate (>5% drop = commit nothing)
          ─► integrity verify (bounded manifest, typed issues)
          ─► derive articles (immutable profiles, V2 fallback by recovered-text share)
          ─► dataset release + index build ticket (3 commits pinned, immutable)
```

## 5. The data anatomy

Two files per publisher, four in the image, plus the encoder.

### 5.1 `index-<publisher>.db` (SQLite, EU ~580 MB)

| table | content | source |
|---|---|---|
| `stamp` | build provenance: commits, dates, enrichment digest, ECDSA-P256 signature with its public key | build |
| `docs` | one row per version per language: the timeline | corpus repo |
| `provisions` | anchors, numbers, headings, `text_sha` pointers | articles repo |
| `text_blobs` | the wording, Brotli, one copy per distinct text, content-addressed (D53) | articles repo |
| `provision_states` | each anchor's distinct texts over time: the article-history axis | derived |
| `citations` | publisher-written cross-references, flattened for reverse lookup | articles repo |
| `fts` (FTS5) | contentless inverted index: token → provisions, BM25 weights title 10 / num 4 / heading 6 / text 1 | derived |
| `work_fts` + siblings | identity: publisher titles and identifiers, plus the 50 reviewed aliases | corpus + alias file |
| `semantic_chunks` | provision text state → vector ordinal | derived |

D53 stores repeated wording once. It trades storage, never provenance: the shared blob holds only
bytes, and every occurrence row knows its work, anchor and date.

### 5.2 `index-<publisher>.vectors` (EU ~320 MB)

`LEXVEC3` header, then N × 384 int8 values. EU holds 777,790 chunk vectors. No text, no
metadata, no trust decisions: geometry only, memory-mapped, consulted exclusively on the hybrid
path.

### 5.3 The encoder

`intfloat/multilingual-e5-small`, pinned by revision and by SHA-256 of the ONNX and tokenizer in
`deploy/embedding-model/model-manifest.json`. Git holds the identity; releases hold the bytes;
the runtime refuses to open ONNX without matching hashes. The same encoder embeds passages at
build and questions at query time, which is why it is pinned: mixed-brain similarities fail
silently.

### 5.4 Who reads what

Nine of the ten MCP tools run on the `.db` alone. Only `search` with `retrieval_mode=hybrid`
touches the vectors, and hybrid is gated off by measurement (§7.1).

## 6. Decisions

The registry (`docs/architecture-program.json`, rendered at `/decisions`) holds the delivery
decisions; the spec holds the numbered design decisions (D41-D84). Every entry records choice,
named alternative, reason, and cost. A sample of the ones that explain the most:

| id | decision | the cost it admits |
|---|---|---|
| D53 | store repeated wording once, content-addressed | readers reconstruct versions through occurrence mappings |
| D54 | run semantic retrieval locally, pinned encoder | Lex owns model packaging and relevance evaluation |
| D55 | Container Apps while the index fits the image; blocked above the gate until a VM path exists | releases can be deployment-blocked |
| D57 | never synthesize consolidation | some works stay metadata-only until the publisher publishes |
| D58 | pin trust outside the artifact | key rotation is an explicit dual-trust release |
| D70 | legal chunk boundaries before hardware optimization | fixed token buckets, cache invalidation on policy change |
| D75 | model-derived names quarantined behind review | enrichment is slow and human-gated |
| D83 | LU subject facets from the publisher's taxonomy, planned | second vocabulary to mirror; French-only labels |
| D84 | match counts and cursor paging for search, planned | a C6 contract amendment, not a patch |

Decision IDs are cited inline in source (`// D48:`, `// D41 collision suffix`), so rationale is
traceable from any line back to the argument.

## 7. Retrieval

### 7.1 Keyword is the default because measurement said so

The 200-case benchmark (11 categories: exact, temporal, conceptual, bilingual, typo, hierarchy,
role, comparison, negative, ambiguity, gap) is split into tuning and holdout sets. On EU holdout:
keyword MRR 0.631 / nDCG@10 0.656; hybrid MRR 0.499 / nDCG@10 0.560. Hybrid wins only recall@10
(0.757 vs 0.730). The activation gate requires hybrid to reach 98% of keyword nDCG; it does not,
so `retrieval_mode` defaults to keyword and no generative or embedding model participates in an
ordinary search.

Why it loses, not only that it loses: the holdout is dominated by the question shapes lawyers
actually ask, identifiers, quoted titles and dated lookups, which BM25 under a title weight of 10
answers almost perfectly, while the small multilingual encoder's one win is recall on conceptual
phrasing (0.757 against 0.730). The numbers therefore point at a per-category opening of the
gate, conceptual and bilingual first with keyword everywhere else, rather than a global flip. Any
such opening faces the same holdout discipline, and until it clears, the default stays keyword.

### 7.2 Identity before ranking

Work resolution decides whether the question names a law, before any ranking. Mention is not
subject: a quoted official title can legitimately contain another law's name (every EU amending
title does). Selection treats the mention that strictly contains every other as the subject;
resolution demotes a work named only inside an amending clause; the reply always names the
instrument, its identifier and date, and discloses the runner-up. Each rule exists because of the
incident in §8.3.

### 7.3 What a result set is, honestly

Results are provisions, deduped by text state in SQL, fused across keyword/fuzzy/(hybrid) ranks,
deduped by (work, anchor), capped at two per work unless the caller scoped to named works, and
budgeted across publishers. Consequences: a "27 hits" answer is what survived five narrowing
stages, not a match count. No total is currently computed and search has no pagination; recorded
as D84 with the reason a SQL OFFSET would be wrong.

## 8. Incidents

One template: symptom, noticed by, why it survived review, fix, guard added, lesson.

### 8.1 The silently dead search
Nightly built an index without the article layer: valid, signed, zero provisions. Caught by an
end-to-end eval question. Guard: the index step takes the article layer as a required input.
Lesson: a green build is not a working system.

### 8.2 The parser that duplicated text
Formex support doubled paragraphs where intro text preceded a list. Caught by re-reading real
output. Guard: frozen profile fingerprints; a published profile's output can never change.

### 8.3 The right article of the wrong law
Full CRR title quoted; EMIR's Article 26 served. The title names two instruments; both resolved
as identity; both hold an art. 26; the residual text rank was near-random; the reply named no
instrument. Survived review because the answer was faithful to the evidence retrieved: no
groundedness metric can catch it. Fixes at three layers (§7.2). Lesson: the dangerous retrieval
failure is a correct answer about the wrong document.

### 8.4 The two-stage nightly failure
1,492 publisher-metadata-only notices first blocked the completeness gate (wrongly counted as
failures), and after that was fixed, overflowed the manifest's 1,000-issue integrity bound: the
corpus committed and still published nothing. Fix: coverage facts recorded on expressions and in
counts, never in the failure list. Lesson: narrowing a gate is not enough when a second bound
guards the same misclassification; and the bound firing is what made the misclassification
visible.

### 8.5 Snippets were empty since the schema was written
`fts5(..., content='')` stores no text, so SQLite's `snippet()` returned NULL on every hit, both
publishers, forever. Proven side by side against a normal table. Fix: windows cut in application
code from the content-addressed store, diacritics-folded to match the tokenizer, 2.2 ms per call
measured before shipping. Lesson: a query that cannot fail loudly will fail invisibly.

### 8.6 The scoped search that returned two rows
The two-per-work fairness cap also applied when the caller had named the works, so "search inside
this law" returned 2 of 40 requested rows. Found by probing the running system with a scoped
query. Guard: the cap lifts only under an explicit works scope; a contract test pins both sides.
Lesson: every fairness rule needs a statement of when it must not apply.

### 8.7 The eval case the code could no longer satisfy
The frozen catalog expected a `navigate` operation the assistant deliberately no longer produces,
and exact-count argument matching was flaky against a planner that legitimately varies its
tuning. Found by asking the running candidate the catalog's own questions before first signing.
Fix: the case expects what the assistant does; reviewed arguments are the load-bearing subset.
Lesson: release evidence must be rehearsed against the running system before it is sworn to.

### 8.8 The deploy gate that distrusted the exit code
Every deploy failed at the final gate for two days, roughly forty minutes per attempt, while the
deployed revision was in fact healthy. The gate exists because an exit code proves the script
ran, and only the monitoring plane proves the revision serves: it asks Azure Monitor for the
candidate's own metrics before the pipeline may report success.

Why it survived so long: each run surfaced exactly one cause, and fixing one per forty-minute
cycle hid the next behind another cycle. The way out was to stop iterating and audit the whole
pipeline in one pass, which found seven distinct causes and fixed them as a batch:

1. The gate read a metric window immediately after revision start, and metric ingestion lags
   minutes behind reality, so an empty series read as a dead revision. Fixed with a bounded
   poll that waits up to twelve minutes for the evidence to arrive ("Wait for the metrics that
   were always going to arrive", #163).
2. Concurrent Azure writes lost management-plane races; a typed retry now wraps the six
   mutating calls and retries only `ConflictingConcurrentWriteNotAllowed` (#162).
3. The GitHub OIDC token expired during the long waits; the run now refreshes its assertion
   mid-flight instead of failing on the next Azure call (#166).
4. Multi-hundred-megabyte index downloads flaked; the fetch became resumable with a low-speed
   abort so a stall fails fast and a retry continues where it stopped (#160).
5. The az CLI crashed printing a U+2713 under a cp1252 console, truncating the very diagnostics
   needed to debug the rest; logs are fetched over REST instead.
6. The container registry lived in a different resource group than the workflow assumed.
7. Carriage returns in a generated path list made a cleanup loop silently match nothing.

Guard added: the gate stayed, unchanged in intent. Removing it was the tempting fix and the
wrong one, because a revision that starts and emits nothing is exactly what it exists to catch.

Lesson, and it is the same lesson twice. Operationally: when a pipeline fails serially, audit it
whole rather than paying a full cycle per cause. Architecturally: "evidence that exists but has
not yet arrived" is a recurring shape in this system. A metric emitted but not yet ingested is
the same situation as a consolidation announced but not yet published (§3.3). In both places the
honest design is a bounded wait plus a truthful status, never removing the check, never guessing.

## 9. How correctness is proven

- **1,017 tests**, including golden snapshots of every public page and tool response (they caught
  every page change made while writing this document), contract tests on the MCP surface,
  architecture fitness rules, and frozen profile fingerprints.
- **Deliberate-break verification**: a new test is trusted only after the fix it pins is removed
  and the test observed failing. Two tests this week passed for the wrong reason and were caught
  exactly this way.
- **The retrieval benchmark** runs inside the index publication workflow; its result is signed
  and published even when the hybrid gate fails, because bad news is publishable and only
  unverifiable news is not.
- **The assistant eval**: 17 frozen cases asserting typed operations, canonical arguments,
  outcomes, population minimums, three injection-canary channels (direct, restored transcript,
  quoted evidence), synthesis presence, segment latencies (first result p95 ≤ 15 s, synthesis
  p95 ≤ 45 s), and an EUR 10 ceiling priced from Microsoft's signed retail meters. LLM-graded
  rubric per case with an independent grader on a separate Azure OpenAI account.
- **Groundedness is not a score.** Every factual sentence in synthesized prose must be a typed
  claim bound to evidence ids, and kind mismatches are rejected in code at request time; a judge
  passes, repairs or refuses the prose; the default reply is not model-authored at all.

## 10. The machinery: pipelines, gates, identities

### 10.1 Workflows and what each one blocks

| workflow | repo | trigger | blocks |
|---|---|---|---|
| `ci` | lex | every PR | merge: tests, goldens, contracts, fingerprints |
| `nightly-fleet` | lex-ops | 02:17 UTC + dispatch | corpus commit: anomaly >5%, determinism, integrity bounds, blocking issues |
| `publish-prebuilt-index` | lex-ops | dispatch, hash-pinned | the release: immutable ticket match, ancestry, SHA pins, stamp verify, KV signature |
| `deploy` | lex | dispatch only | traffic: structurally cannot promote |
| `publish-assistant-evaluation` | lex-ops | dispatched by a passing eval | evidence: revalidates against the exact revision, browser gate |
| `revision-traffic` | lex | dispatch | production: seven verified files, human signature, revision identity |

### 10.2 The chain

```
PR ──ci──► main ──nightly──► corpora + articles + TICKET(3 commits)
                                   │
                local index build ─┤ (owner's machine; a one-person-team
                                   │  decision, recorded with its risks)
                                   ▼
              publish-prebuilt-index [OIDC + kv-lex-soufien signs]
                                   ▼
              deploy ──smoke──► candidate at 0% traffic ──✗ cannot promote
                                   ▼
              eval (17 cases, canaries, p95 gates, EUR ceiling)
              + human attestation [kv-lex-eval-review]
                                   ▼
              publish-assistant-evaluation ──► five-file evidence release
                                   ▼
              revision-traffic ──verify all──► traffic moves
```

### 10.3 Separation of authority

| authority | identity | deliberately cannot |
|---|---|---|
| merge code | owner through branch policy | bypass checks silently |
| write corpora | nightly OIDC | touch code branches |
| sign artifacts | `uami-lex-publisher` + `kv-lex-soufien` | approve evaluation cases |
| approve cases | owner's Entra identity + `kv-lex-eval-review` | be exercised by any workflow: the publisher identity has no access to this vault |
| deploy | `uami-lex-deploy` | move traffic without the evidence set |
| runtime | `uami-lex-runtime` | write anything: indexes are baked, read-only bytes |

No automated actor can approve its own release. That is the design's one-sentence answer to AI
governance, implemented as vault ACLs rather than policy prose.

### 10.4 Azure inventory (resource group `rg-platform`, France Central)

| resource | role |
|---|---|
| `cae-platform-law` + managed certificate | Container Apps environment for law.soufien.lu |
| `ca-lex-web` | the one app: 1 vCPU, 2 GiB, one always-on replica, revisions as immutable code+data units |
| `stlexindexes` | Blob: private staging (hash-pinned uploads) and immutable releases |
| `kv-lex-soufien` | artifact signing key (`keyvault-lex-v2`), OIDC-gated |
| `kv-lex-eval-review` | the human-approval key, RBAC excludes the publisher identity |
| `uami-lex-publisher` / `uami-lex-deploy` / `uami-lex-runtime` | the three separated identities of §10.3 |
| `ai-lex-web` | Application Insights via OpenTelemetry |
| `soufien.lu` DNS zone | the domain |
| (adjacent groups) | container registry; Azure OpenAI: candidate/planner models on one account, the eval grader on a different account, deliberately |

### 10.5 The image is the product

The Docker build compiles the web bundle and binaries, downloads the published index releases,
and verifies every manifest signature against pinned trust roots inside the build: an image with
tampered indexes fails to build. The shipped container holds binaries, both `.db`, both
`.vectors`, the encoder, manifests and trust roots. A revision therefore pins code and data
together; rollback rolls both back; the only external runtime dependency is Azure OpenAI, for
the assistant only.

### 10.6 Cost posture

Public repositories on free standard runners; heavy builds on the owner's machine via the
hash-pinned ticket (the reason "prebuilt" exists); a EUR 10 eval ceiling priced from Microsoft's
retail meters with no override flag; one always-on replica as the paid runtime.

### 10.7 What survives losing what

Continuity is a property of the layout, not a promise. Code, spec and both corpora are public
git: losing any single machine, the build machine included, loses no evidence and no history.
The image is immutable and rebuildable from a signed release manifest; the previous revision
stays deployable throughout. The signing key is non-exportable in Key Vault, so a lost key is
not a leaked key; recovery is the explicit dual-trust rotation D58 describes, published as a
release rather than performed quietly. A GitHub outage degrades to the failure mode the gates
were designed around: the nightly commits nothing and the site keeps serving yesterday's law.
The one asset that is neither public nor signed is the local embedding cache; losing it costs a
full re-embed under the D69/D70 batch discipline, compute rather than data.

### 10.8 Capacity triggers, and the plan when one fires

None of this is built. Each row is a threshold that is already observable with what the system
records today, the change that threshold authorises, and the cost that change admits. The
thresholds are targets, not measurements; the figures in brackets are measured. Writing them
down is the point: the next architecture move is decided by a number rather than by a feeling,
and a move made before its trigger fires is a move made without evidence.

| trigger | what it means | the move | what it costs |
|---|---|---|---|
| MCP queue-deadline rejections appear at all (admission is 8 executing, 16 queued, 2 s deadline) | concurrency is bounded by the per-publisher execution gate rather than by CPU: a tool call with no publisher argument holds every publisher's semaphore for its whole execution | narrow the gate to the encoder and vector sections, where the non-preemptable work actually is; keyword search, `as_of` and `diff` already run on isolated SQLite sessions | one contained change plus a contract test, no new infrastructure |
| more than 50 assistant users a day, or the global daily cap reached three days running, or Azure OpenAI answering 429 | demand exceeds the model budget rather than the machine | raise the Azure OpenAI TPM quota and the daily cap together, in that order; consider provisioned throughput only after measuring sustained utilisation | pay-as-you-go rises linearly, provisioned throughput is a fixed monthly floor that is cheaper only above a measured utilisation |
| served p95 above 800 ms sustained (deploy gate is 1,500 ms; measured 456 ms at 60 rpm) | the single replica is saturating | move the quota ledger and the idempotency registry to a shared store, then add replicas | the ledgers stop being authoritative in process memory; the index needs no change, because the artifact is immutable, signed and content-addressed, so every replica serves identical bytes |
| peak working set above 1.2 GB (gate is 1.5 GiB, measured peak 498 MB) | the memory-mapped index no longer fits beside the runtime | D55's next rung: the release moves from the image onto a VM-managed local disk | a second verified deployment path and OS patching, which is why D55 gates it rather than assuming it |
| a third publisher is admitted | the required-publisher set is stated in four places and MCP fans out per publisher under an 8-publisher budget | unify the publisher set to one source, then re-measure fan-out latency before admitting a fourth | one refactor; the fan-out cost is real and is the reason the budget exists |

What no trigger authorises: a rewrite, a framework, or splitting the process. The measured
bottlenecks are a lock scope and two in-process ledgers, and both are contained changes.

## 11. Known limits

The full record with effects, fixes, gains and why-not-now lives in `docs/known-defects.md`.
The short honest list:

1. **Positional version identity** can, on a Legilux same-date tie re-key, serve one
   consolidation's text under another's interval, undetectably (13 tie pairs exist). Highest
   open severity; needs a corpus-format change and re-ingest.
2. **Empty provisions**: 6,424 of 537,035 (1.2%), counted per document and flagged per version
   since 2026-08-13; the worst case (the 1993 financial-sector law, 105 of 145) was recovered by
   the second Memorial profile the same day. Ratchet gate not yet built.
3. **LU text gap is upstream**: 1,493 announced consolidations with no publisher manifestation in
   any format, proven by the publisher's own catalogue.
4. **`work_resolution_status` conflates** "named no law" with "named one we failed to resolve";
   fixing it needs a citation-shape detector, and the benchmark to prove it helps.
5. **No search pagination or match count** (D84): a contract amendment, not a patch.
6. **Observability**: 17 diagnostic codes without request-id correlation; release-time gates are
   strong, production-time telemetry is thin.
7. **One replica**: the quota ledger's authority and the memory-mapped index make horizontal
   scale a redesign (per-replica memory), accepted while measurements stay far below limits.
8. **A flaky timing test** (`Preparation_heartbeat...`) failed once on a loaded runner and passed
   on rerun; the repo forbids fixing it by raising the timeout, so it stays listed until fixed
   properly.

## 12. Deliberately not built

- No consolidation synthesis, no entity extraction, no model-derived names in identity: the
  never-generate rule outranks completeness.
- No replanning loop: measured failures were identity and data failures, not plan-shape failures.
- No LangChain/LlamaIndex: the failures documented in §8 were findable because every layer is
  hand-written and typed.
- No cross-publisher joins: LU and EU identity spaces are disjoint; saying so beats approximating.
- No bi-temporal query surface yet: the audit axis is stored and provable via `provenance`;
  "as we knew it on T" waits for a real user.
- No UI-layer machine translation of provisions: an English rendering of a French statute would
  be the one model-generated text a reader could mistake for law. Orientation stays in the
  interface language; wording stays the publisher's.

## 13. Mapping to the common RAG decomposition

Much recent writing decomposes document QA into four bricks: parse, understand the question,
retrieve, generate, each behind a typed contract. Lex fits, with two deviations that are the
point:

| brick | Lex's counterpart | deviation |
|---|---|---|
| parsing | the nightly: ingest + immutable extraction profiles + derived articles | it is a *temporal corpus pipeline*, not per-document parsing; profiles are versioned evidence |
| question parsing | work resolution + article/role/date intent + typed operation plan | identity resolution is a separate, earlier stage than ranking, and the hardest one (§8.3) |
| retrieval | FTS5/BM25 default, gated hybrid, filters, fusion, caps | the fashionable half is built and measurably off |
| generation | typed operations first; prose optional, claim-typed, judged | the default answer contains no generated sentence at all |

And one addition the four-brick frame lacks: a fourth retrieval failure mode. Beyond
not-retrieved, wrong-passage and buried, there is *right passage, wrong document*: identity
failure upstream of ranking, invisible to groundedness metrics because the answer is faithful to
the evidence retrieved. §8.3 is its case study, and §7.2 its fixes.
