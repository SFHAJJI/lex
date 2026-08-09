# Implementation plan: Publisher-first retrieval

## Overview

Complete the stalled work-catalog implementation, repair the shared retrieval path, retain official
publisher discovery metadata, migrate away from code-supplied common names, then graduate only the
measured parts of enrichment and Retrieval Agent v2. Rebuild and promote signed artifacts only after
the corrected benchmark and runtime gates pass.

## Architecture decisions

- Exact identity is limited to official identifiers, unique normalized official titles, and
  reviewed collision-free aliases.
- Publisher short titles and classifications are stronger than model metadata but remain discovery
  signals unless individually approved for exact identity.
- One signed index/vector pair may contain separate trust classes and digests. Physical sidecars or
  runtime enrichment services are rejected.
- Query decomposition produces work constraints, article/role intent, and a residual provision
  query before provision retrieval.
- Existing `/coverage`, stamps, SQLite, FTS5, ONNX vectors, MCP, and deployment workflows are reused.

## Dependency order

```text
recoverable WIP checkpoint
  -> enrichment/index contract
  -> shared query decomposition and retrieval
  -> publisher metadata ingestion
  -> clean corpus migration
  -> resolver and agent ownership
  -> corrected benchmark
  -> signed artifacts
  -> candidate deploy and promotion
```

## Phases

### Phase 1: Foundation

1. Review and finish the stalled mixed-vector/work-catalog WIP.
2. Correct `lex-work-enrichment/1` before release and implement its deterministic producer.
3. Add query decomposition and route all public search surfaces through shared retrieval semantics.

### Checkpoint 1

- Focused tests, full .NET tests, and web build pass.
- Weak discovery remains inactive for the current assistant.
- Each slice is DCO committed and recoverable.

### Phase 2: Publisher-first discovery

4. Retain FR/EN Cellar short titles, EuroVoc/directory identifiers and labels, and document role.
5. Add reviewed data overrides only for verified publisher gaps/errors.
6. Re-ingest/re-derive EU records, prove legacy `CommonNames` contamination is absent, then remove
   the code table.
7. Extend the existing coverage build inventory and resolver status contract.

### Checkpoint 2

- Publisher-only indexes resolve exact names and rank descriptive candidates on the frozen set.
- Protected publisher text/hash inventories are unchanged.
- Ambiguity and unavailable states cannot be reported as corpus gaps.

### Phase 3: Measured enrichment and agent

8. Repair the benchmark identity model and add FR/EN EU/LU positive, negative, ambiguity, role,
   comparison, and gap cases.
9. Admit bounded offline LLM concepts only if a held-out ablation proves incremental value.
10. Implement typed Retrieval Agent v2 with raw-user resolution first and claim-typed validation.

### Checkpoint 3

- No false auto-selection on ambiguity, unknown works, or corpus gaps.
- Unaffected-query, latency, size, memory, cold-start, and vector-mapping gates pass.
- A fresh-context review finds no unresolved critical issue.

### Phase 4: Release

11. Complete five-axis review, simplification, ADR/spec updates, migration notes, and rollback plan.
12. Push reviewed commits, pass CI, build signed EU/LU artifacts, and verify manifests.
13. Deploy a zero-traffic candidate, run critical smokes, promote, and verify live behavior/logs.

## Risks and mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Publisher short titles are incomplete or dirty | False identity | Discovery-only default, quality checks, reviewed exact promotion |
| Historical titles leak into current resolution | Wrong version/language | Preserve expression language/time and restrict exact resolution |
| Weak concepts dominate ranking | Wrong work selection | Bounded weight/count, provenance, ablation, no exact resolution |
| Corpus rebuild changes protected material | Trust break | Full canonical protected-row/hash comparison before publication |
| Linear work-vector scan grows | Latency/memory regression | Per-kind caps and full-corpus benchmark gates |
| Search surfaces drift | Different user outcomes | One request contract and parity tests |
| Artifact or deploy fails | Production outage | Fail closed, zero-traffic candidate, retained prior revision |

## Rollback outline

- Code: retain prior production revision and immutable image.
- Retrieval activation: publisher/work discovery and model weak fields remain separately gated.
- Artifacts: retain previous signed EU/LU release manifests and image.
- Corpus migration: publish new immutable corpus commits; never rewrite or delete prior releases.

## Open questions

None requiring user input before Phase 1. Repository or publisher contradictions stop the relevant
slice for explicit resolution.

---

## Persistent assistant shell — implementation plan

### Slice A: policy and shared controller

1. Freeze route eligibility, session-state and typed-result navigation as pure tests.
2. Extract the existing conversation/history/clarification flow into one controller used by both
   workspace and standalone mounts.

Checkpoint: current assistant tests plus the new policy/controller tests pass; no API changes.

### Slice B: responsive shell and server mount

3. Change the launcher to a fixed secondary action with a one-time hint and tab-scoped state.
4. Dock desktop content through a body state class; render a modal sheet/backdrop with focus
   containment on narrower screens.
5. Emit `#assistant-root` and the existing bundle only on research pages; mount exactly one
   assistant root per document.

Checkpoint: web build, bundle smoke and ASP.NET route tests pass.

### Slice C: release

6. Verify desktop/mobile runtime behavior with Chrome DevTools, run accessibility and console/
   network checks, then complete correctness/simplification/security review.
7. DCO commit, push, deploy through the repository's existing production workflow and verify live
   research and excluded routes plus rollback identity.

Risks are bounded by reusing the existing API/controller, mapping only typed effects to workspace
URLs, loading no new dependency, and retaining the current deployment rollback path.
