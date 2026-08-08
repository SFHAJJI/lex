# Experiment tasks

## Task 1: Baseline and scenario manifest

- [x] Copy full index shadows and the pinned model into a new experiment output directory; fixture
      extraction follows from this immutable baseline.
- [x] Record source commits, file hashes, model hashes, machine details, protected-table hashes,
      representative reads/diffs, and baseline retrieval results.
- [x] Add machine-readable scenarios for exact, descriptive, ambiguous, temporal, and gap cases.
- Verify: manifest validation passes and original release hashes remain unchanged.

## Task 2: Enrichment workbench contract

- [x] Write failing tests for allowed proposal fields, evidence requirements, normalization, and
      alias collisions.
- [x] Implement deterministic publisher extraction and provenance-aware accepted export.
- [x] Prove forbidden legal fields cannot pass validation.
- Verify: focused unit tests and deterministic export hash pass.

## Task 3: LLM discovery enrichment sample

- [x] Generate common-name, description, concept, synonym, and practice-area proposals for the
      fixture works without writing protected legal fields.
- [x] Record deployment, prompt hash, evidence, timestamp, and rejection reasons.
- [ ] Test repeatability and evidence anchors; review strong aliases and export accepted weak
      discovery fields separately from rejected proposals.
- Verify: rerunning index construction from the accepted export is deterministic.

## Task 4: Work-level retrieval experiment

- [ ] Write failing ranking and authoritative-identity tests.
- [ ] Add the smallest work-level FTS experiment that passes them.
- [ ] Compare FTS-only with FTS plus one work discovery vector; measure tiny and full-shadow
      latency, size, descriptive recall, and hybrid exact-name behavior.
- Verify: exact/alias/title matrix passes and hash/read/diff invariants pass.

## Task 5: Typed-plan Agent Framework variant

- [ ] Define typed plan/clarification contracts and compact MCP execution results.
- [ ] Run all descriptive and ambiguous scenarios three times.
- [ ] Record tool paths, result ranks, latency, tokens, and failures.
- Verify: results JSON validates and no run is omitted.

## Task 6: Direct tool-calling Agent Framework variant

- [ ] Expose compact read-only functions over the same `McpCore`.
- [ ] Apply deterministic subject pre-resolution and two-search cap.
- [ ] Run the same scenarios and metrics three times.
- Verify: comparable results JSON validates and tool calls stay within limits.

## Task 7: Citation, judge, and memory gates

- [ ] Add deterministic citation/work/date/anchor validation.
- [ ] Test conditional judge pass, repair, and refusal cases.
- [ ] Test bounded session continuation and restoration after process restart.
- Verify: no unsupported claim remains and temporal follow-ups do not leak.

## Task 8: Jurisdiction-first result prototype

- [ ] Build fixture-driven jurisdiction sections with passages nested under laws.
- [ ] Hide irrelevant jurisdiction facets and preserve URL state.
- [ ] Test responsive, keyboard, loading, error, empty, and count behavior.
- Verify: frontend tests/build pass and screenshots cover four target widths.

## Task 9: Graduation and production handoff

- [ ] Compare retrieval, agent, grounding, latency, token, size, and UX evidence.
- [ ] Write the ADR draft with selected and rejected alternatives.
- [ ] Produce production tasks without copying experiment implementation wholesale.
- [ ] If gates pass, start the clean production implementation, signed rebuild, controlled Azure
      rollout, and live validation.
- Verify: every decision links to a reproducible measurement or is marked unmeasured.
