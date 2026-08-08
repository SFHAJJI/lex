# Publisher-first retrieval tasks

- [x] Task 1: Finish and review the stalled work catalog and mixed-vector mapping
  - Acceptance: contiguous typed vector mappings; exact/weak ranking separation; current assistant
    cannot auto-select from weak discovery.
  - Verify: focused `WorkSearchTests` and `IndexTests`, then full .NET suite.
  - Files: `src/Lex.Index/*`, `tests/Lex.Tests/*`

- [x] Task 2: Finalize and produce `lex-work-enrichment/1`
  - Acceptance: canonical schema, provenance, reviewer approval, evidence/content binding, and a
    deterministic producer whose output the strict consumer accepts.
  - Verify: producer byte-repeat test, rejection tests, ingest integration test.
  - Files: `src/Lex.Ingest/*`, `src/Lex.Index/WorkSearch.cs`, `tests/Lex.Tests/*`

- [x] Task 3: Decompose queries and unify public retrieval semantics
  - Acceptance: names/articles/roles/residual terms are separated; SPA, no-JavaScript, MCP, and
    assistant defaults are consistent; exact names do not hide requested provision retrieval.
  - Verify: RED/GREEN query tests and cross-surface contract tests.
  - Files: `src/Lex.Index/*`, `src/Lex.Mcp/*`, `src/Lex.Web/CatalogueEndpoints.cs`, tests

- [x] Checkpoint 1: full .NET tests and web build pass; DCO commits are clean

- [ ] Task 4: Retain publisher discovery metadata and document role
  - Acceptance: FR/EN short titles, EuroVoc/directory identifiers and labels, language/time,
    provenance, and controlled role survive publisher-to-index flow.
  - Verify: adapter contract tests against pinned fixtures and index query tests.
  - Files: `src/Lex.Sources.EurLex/*`, `src/Lex.Law/*`, `src/Lex.Ingest/*`, tests

- [ ] Task 5: Migrate off `CommonNames`
  - Acceptance: reviewed data overrides cover verified gaps/errors; code table is removed only after
    clean re-ingest; no derived publisher field contains a legacy code alias.
  - Verify: migration invariant, corpus/index comparison, professional-name evals.
  - Files: EUR-Lex source/corpus tooling, enrichment data, tests, migration docs

- [ ] Task 6: Complete resolver and coverage states
  - Acceptance: each named entity returns resolved/ambiguous/unresolved/unavailable; multi-work
    queries work; signed build inventory exposes freshness and known failures without overclaiming.
  - Verify: collision, outage, comparison, and coverage tests.
  - Files: `src/Lex.Index/*`, `src/Lex.Mcp/*`, `src/Lex.Ask/*`, tests

- [ ] Checkpoint 2: publisher-only retrieval and protected-content gates pass

- [ ] Task 7: Correct and expand the retrieval benchmark
  - Acceptance: canonical collection/work identity; FR/EN EU/LU positives, negatives, ambiguity,
    role, comparison, and gap cases; separate tuning and holdout sets.
  - Verify: benchmark self-tests and frozen baseline report.
  - Files: `src/Lex.Index/RetrievalBenchmark.cs`, `evals/*`, tests, docs

- [ ] Task 8: Graduate bounded weak enrichment only if ablation passes
  - Acceptance: per-work/per-kind caps, lifecycle/revalidation, ranking and rollback gates; no exact
    resolution or citation path; measurable holdout gain.
  - Verify: publisher-only versus enriched ablation plus size/memory/latency gates.
  - Files: workbench/producer, `src/Lex.Index/*`, tests, reports

- [ ] Task 9: Implement Retrieval Agent v2
  - Acceptance: raw-user resolution precedes planning; generated names remain candidates; claim-
    typed evidence and coverage disclosure gate answers.
  - Verify: selected experiment scenarios plus new ambiguity/gap/adversarial cases.
  - Files: `src/Lex.Ask/*`, `src/Lex.Mcp/*`, tests/evals, docs

- [ ] Checkpoint 3: full builds, reviews, security/performance gates, and ADRs pass

- [ ] Task 10: Build and promote signed production artifacts
  - Acceptance: fresh corpus inputs, deterministic protected inventory, valid manifests, corrected
    benchmarks, and retained previous artifacts.
  - Verify: artifact verification commands and candidate runtime smokes.

- [ ] Task 11: Deploy and verify production
  - Acceptance: zero-traffic candidate passes, promotion succeeds, live critical flows and logs are
    healthy, artifact/code identities match, rollback revision remains ready.
  - Verify: GitHub run, Azure revision state, live health/MCP/search/assistant checks.
