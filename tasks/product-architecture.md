# Product architecture release checklist

Source: `docs/product-architecture-review.md`

Plan: `tasks/product-architecture-plan.md`

Status: accepted and in progress

## Gate 0: executable target and release path

- [x] Audit current main and production across product, data, retrieval, assistant, UI,
  accessibility, security, operations, evaluation and documentation.
- [x] Write the target architecture and release contract.
- [x] Complete independent Codex and Claude adversarial reviews.
- [x] Reconcile every P0/P1 into the accepted specification or an explicit trade-off.
- [x] Record the maintainer verdict and implementation ownership.
- [ ] Audit reachable `lex-ops` history and make the repository public safely.
- [ ] Verify approved public workflows use free standard runners and bounded storage.
- [ ] Add `promote=false` and separate promotion/rollback operations to the production OIDC path.
- [ ] Prove signed prebuilt publication, unchanged traffic, promotion and rollback on the production
  Container App without a staging environment.

## Increment 1: restore assistant authority

- [ ] Add failing exhaustive tool/status/outcome tests and the explicit MCP 2.0 migration contract.
- [ ] Introduce the internal operation plan and separate legal/transport outcomes.
- [ ] Give every operation in a compound request an order-invariant authoritative outcome.
- [ ] Add required-publisher readiness and scoped-population disclosure.
- [ ] Make work authorization fail closed and test every approved authority source.
- [ ] Make comparison, missing-publisher and provisional gaps dominate supporting evidence.
- [ ] Render exact, comparison, ranking, inventory, timeline, verification, clarification, legal
  boundary and gap replies from localized typed templates.
- [ ] Restrict composer and judge calls to bounded descriptive synthesis.
- [ ] Preserve the typed result on composer, judge, cancellation and transport failure.
- [ ] Resolve work identity only when the selected operation requires it.
- [ ] Carry bounded temporal, provenance and verification evidence into every legal UI effect.
- [ ] Run focused tests, full .NET tests, web tests and build.
- [ ] Complete code-quality and adversarial reviews with no unresolved P0/P1 finding.
- [ ] Commit the authority slices atomically.

## Increment 2: stream and bound the system

- [ ] Emit versioned request-ID `operation_result` before optional synthesis.
- [ ] Add idempotency, cancellation and stale-result suppression.
- [ ] Remove automatic duplicate non-idempotent POST fallback.
- [ ] Implement bounded carried context, atomic clarification and workspace-preserving reset.
- [ ] Reject overlong current questions rather than silently truncating them.
- [ ] Fix quota admission order and pin production to one replica.
- [ ] Enforce streamed request bytes and bounded MCP inputs/outputs.
- [ ] Enforce and load-test MCP execution, queue, hybrid and rolling-rate ceilings.
- [ ] Add truthful data-flow disclosure, logging allowlist and prompt-injection tests.
- [ ] Run focused tests, full .NET tests, web tests and build.
- [ ] Commit server and browser stream changes separately.

## Increment 3: make the product legible

- [ ] Pin Playwright, axe-core and Lighthouse harnesses and budgets.
- [ ] Add endpoint-to-route-ledger fitness test and fail-closed golden discipline.
- [ ] Add failing tests for desktop complementary and mobile modal assistant semantics.
- [ ] Correct assistant landmark/dialog markup and launcher collision spacing.
- [ ] Add an accessible `Check the work` disclosure to desktop and mobile navigation.
- [ ] Link the homepage engineering path to `/built` and its evidence routes.
- [ ] Make `/developers` canonical and redirect `/ai`.
- [ ] Preserve `/about`, `/stories`, `/find`, `/changed` and no-JavaScript navigation.
- [ ] Preserve the editorial visual language and flat catalogue source classes.
- [ ] Clarify record-only gaps, provisional evidence and publisher timeline semantics.
- [ ] Test all ledger routes at desktop, 1,099/1,100 and 320 px widths in Chrome.
- [ ] Check keyboard, touch, zoom, forced colors, light/dark, overflow, console, network and history.
- [ ] Complete accessibility and code-quality reviews with no unresolved P0/P1 finding.
- [ ] Commit behavior, golden families and public copy separately.

## Increment 4: prove ingestion and behavior

- [ ] Add typed retry, metadata-only, incomplete-enumeration and prior-clean-artifact contracts.
- [ ] Expand deterministic assistant contract cases for every operation and failure class.
- [ ] Replace reply-exists smoke with exact, aggregate, comparison-gap and clarification outcomes.
- [ ] Make live assistant evaluation fail closed with frozen clock and negative verifier tests.
- [ ] Sign the evaluation identity, enforce token/cost budgets and bind it to the candidate.
- [ ] Migrate to blind-reviewed retrieval benchmark v3 with non-vacuous collection strata.
- [ ] Preserve v2 rejection evidence and keyword retrieval as the default.
- [ ] Update README and public evidence pages only after implementation measurements exist.
- [ ] Add or amend the public architecture decision for operation and answer policy.
- [ ] Establish formatter baseline or changed-lines gate in a separate commit.
- [ ] Commit ingestion, evaluation, benchmark and documentation slices separately.

## Increment 5: release and verify

- [ ] Verify no workflow consumes paid GitHub Actions minutes or unbounded storage.
- [ ] Run local build, test, audit, format, browser, artifact and deployment gates.
- [ ] Review every automated PR comment and address every actionable finding.
- [ ] Build or select verified production artifacts and retain the current rollback artifact.
- [ ] Promote through the approved no-charge deployment path.
- [ ] Verify production routes, legal operations, assistant cases, accessibility, logs, readiness,
  artifact identity, benchmark identity and rollback readiness.
- [ ] Update public decision/status evidence from measured production facts.
- [ ] Rehearse restoration of the previous exact revision and manifest set, then restore final state.
- [ ] Audit all public documentation and portfolio content consumers for consistency.
- [ ] Inventory old worktrees, preserve dirty/unmerged work and remove only approved obsolete or
  generated artifacts.

## Completion definition

- [ ] Every release gate in section 12 of the specification passes.
- [ ] No unresolved P0/P1 review finding or actionable PR comment remains.
- [ ] Production behavior and public claims agree.
- [ ] Keyword remains the default unless a signed v3 holdout passes all activation gates.
- [ ] The deployed code, image, corpus, index, model, report and rollback identities are recorded.
