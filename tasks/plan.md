# Implementation Plan: Retrieval, enrichment, and legal-research agent experiment

## Architecture decisions

- The corpus is authoritative; the current index is an inventory and baseline, not an enrichment
  source of truth.
- The candidate database is disposable. Only provenance-aware accepted additions and overrides
  may graduate into a production index build input.
- Work resolution and provision retrieval remain separate index concerns.
- `McpCore` owns legal retrieval semantics; Agent Framework receives compact views of its evidence.
- Microsoft Agent Framework is required; the experiment selects between clean orchestration
  shapes rather than between framework and handwritten chat control.
- Exact navigation bypasses generation. Only descriptive questions enter the agent experiment.
- UI information architecture is evaluated after retrieval contracts stabilize.

## Dependency graph

```text
baseline snapshot and fixtures
  -> authoritative metadata extraction
  -> proposal/review workbench
  -> experimental work-level index
  -> deterministic retrieval evaluation
  -> Agent Framework A/B evaluation
  -> citation/judge/session evaluation
  -> jurisdiction-first UI prototype
  -> evidence report and graduation decision
```

## Phase 1: Reproducible baseline

- Build a manifest for copied fixture data and copied full-index shadows.
- Capture baseline queries, hashes, representative reads, and comparisons.
- Add a machine-readable scenario set before implementation.

### Checkpoint

- Every input has an immutable identifier; release artifacts remain unchanged.

## Phase 2: Enrichment workbench

- Extract publisher/deterministic fields into a candidate schema.
- Add allowed-field validation, normalization, collision detection, and evidence provenance.
- Generate a small LLM-derived discovery sample and validate its evidence, repeatability,
  collisions, field ownership, and rank tier.
- Review aliases before exact-name rank; keep accepted model descriptions/concepts in a labelled
  weak discovery export.
- Prove legal text and legal-effect fields cannot be exported as enrichment.

### Checkpoint

- Approved overrides are deterministic JSON; rejected proposals and reasons are retained.

## Phase 3: Deterministic retrieval

- Add work-level FTS to copied/tiny indexes and compare it with one optional work discovery vector
  per work; never contaminate provision vectors.
- Test the ranking contract and alias containment in longer queries.
- Measure full-shadow latency and index-size delta.
- Compare authoritative hashes and read/diff output with the baseline.

### Checkpoint

- Named navigation is correct and fast; authoritative identity is unchanged.

## Phase 4: Agent Framework A/B

- Implement a typed-plan harness and deterministic MCP executor.
- Implement a single tool-calling harness over compact `McpCore` evidence.
- Add deterministic subject pre-resolution to both.
- Run repeated named, descriptive, ambiguous, temporal, negative-evidence, and corpus-gap cases.
- Record correctness, ranks, calls, latency, tokens, and tool-result sizes.

### Checkpoint

- Select one execution shape; if both miss a gate, iterate within Agent Framework and record the
  failure rather than falling back to unstructured chat code.

## Phase 5: Grounding and memory

- Validate every cited work/date/anchor/permalink deterministically.
- Run the conditional judge only on synthesized prose and test repair/refusal behavior.
- Test bounded Agent Framework session serialization/restoration across a simulated restart.
- Define retention and no-durable-memory behavior for the future public service.

### Checkpoint

- No unsupported legal claim survives; temporal follow-ups remain scoped correctly.

## Phase 6: Result and facet prototype

- Render jurisdiction sections with law rows and nested matching passages from fixtures.
- Hide irrelevant facets per jurisdiction and preserve URL state.
- Test keyboard behavior, responsive layouts, empty/error/loading states, and result counts.

### Checkpoint

- Lawyers can understand source jurisdiction and narrow scope without encountering irrelevant
  controls or duplicated results.

## Phase 7: Evidence and graduation

- Publish an experiment report with all failures and limitations.
- Record the winning/rejected alternatives as an ADR draft.
- Produce production-sized tasks only for behavior that passed.
- Remove or archive the disposable worktree; never merge it wholesale.

## Risks and mitigations

| Risk | Impact | Mitigation |
|---|---:|---|
| LLM proposes plausible but false metadata | High | Protected fields, evidence anchors, repeatability/collision rules, reviewed strong aliases, weak labelled discovery fields |
| Agent looks correct once but is unstable | High | Three repetitions and machine-readable pass criteria |
| Tool context creates excess cost/latency | High | Compact DTOs, two-search cap, exact navigation bypass |
| Corrigenda/amending acts outrank base acts | High | Document-role tests and explicit-query exceptions at retrieval owner |
| Enrichment changes legal output | Critical | Hash/read/diff invariants before ranking evaluation |
| UI groups overlapping metadata | Medium | Only jurisdiction groups; other dimensions stay facets |
| In-memory sessions disappear on scale-to-zero | Medium | Serialize/restore experiment and bounded retention contract |

## Experiment commands

Commands are added only with the harness that owns them. Every command must expose inputs, outputs,
and overwrite behavior; no implicit production path is allowed.
