# Implementation plan: Lex product architecture release

Source: `docs/product-architecture-review.md`

## Objective

Ship the accepted operation-controller architecture without replacing the signed ingestion, local
index, keyword retrieval or public MCP foundations. Make assistant authority deterministic, expose
typed results before optional synthesis, make partial-corpus and evidence gaps impossible to hide,
and prove the complete product in a real browser before a reversible production promotion.

## Commands

```powershell
dotnet build Lex.slnx -c Release
dotnet test tests/Lex.Tests/Lex.Tests.csproj -c Release
Set-Location web
npm test
npm run build
```

Browser, evaluation and release commands are added and pinned by their owning increments before
those increments may complete. Intended golden changes require `LEX_GOLDEN_UPDATE=1`, a reviewed
per-family diff and a clean working directory after the test.

## Dependency order

```text
accepted spec and route ledger
  -> public free-workflow and rollback path
  -> required publisher readiness
  -> operation and status contracts
     -> work authority
     -> deterministic reply policy
     -> versioned result stream
        -> conversation and browser state
        -> quota, bounds and privacy
  -> route fitness and browser harness
     -> assistant shell and public navigation
  -> ingestion completeness and evaluation integrity
  -> signed artifacts, candidate, promotion and rollback
```

## Increments

### 0. Release foundation

1. Commit the accepted specification, review reconciliation, route ledger and this plan.
2. Audit reachable `lex-ops` history, make the repository public and verify free standard runners.
3. Add `promote=false` and explicit promote/rollback operations to the existing production OIDC
   path, then prove local-prebuilt publication, Key Vault signing, zero-traffic verification and
   exact rollback without assuming a staging environment.

Checkpoint: no paid Actions path, no secret exposure, signed zero-traffic production-app candidate
with unchanged traffic, and rehearsed rollback.

### 1. Assistant authority and corpus readiness

1. Add exhaustive internal tool/status/legal/transport mappings and a public MCP 2.0 migration for
   the intentional safety-bound changes.
2. Add required-publisher readiness and scoped aggregate population disclosure.
3. Make work authority deny by default and implement exact, direct-evidence, clarification and
   bounded carried-authority sources.
4. Add localized deterministic replies, evidence context, provisional disclosure and the
   application-owned legal boundary.
5. Preserve typed results when composer, judge, cancellation or transport fails.

Checkpoint: all deterministic contract cases pass with model clients mocked; full .NET suite and
MCP compatibility corpus pass.

### 2. Stream, context and public boundaries

1. Emit versioned request-ID `operation_result` events before optional synthesis.
2. Implement idempotency, cancellation and stale-result suppression; remove duplicate POST fallback.
3. Implement workspace-preserving reset, atomic clarification and bounded conversation authority.
4. Fix quota admission, pin one replica, enforce streamed bytes, bound MCP inputs/outputs and add
   MCP execution, queue, hybrid and rolling-rate admission controls.
5. Add truthful Azure-processing disclosure, logging allowlist and injection-canary tests.

Checkpoint: blocked composer cannot delay or erase the workspace; concurrency, bounds, privacy and
web client tests pass.

### 3. Product legibility and browser proof

1. Pin Playwright, axe-core and Lighthouse and add endpoint-to-route-ledger fitness tests.
2. Correct desktop dock and mobile modal semantics, launcher clearance and reset behavior.
3. Add the `Check the work` disclosure, engineering homepage path and canonical developer surface.
4. Preserve and expose about, stories, find, changed and no-JavaScript routes.
5. Clarify flat legal classes, record-only gaps, publisher timeline axes and provisional text.

Checkpoint: the complete viewport, keyboard, touch, zoom, forced colors, route, console, network and
back-forward matrix passes against the fixture container.

### 4. Ingestion and measured evidence

1. Enforce typed retry, metadata-only, incomplete-enumeration and prior-clean-artifact policies.
2. Make assistant evaluation fail closed with frozen clock, complete grading, signed identity and
   numeric call/token/cost/latency bounds.
3. Blind-authored retrieval benchmark v3 with non-vacuous collection strata while preserving v2
   failed-candidate evidence and keyword default.
4. Update every public engineering and scope claim only from measured served facts.

Checkpoint: negative verifier tests pass, reports bind to candidate identities, and public pages
render compatible historical and current evidence.

### 5. Release and consistency

1. Refresh corpora and build resumable indexes locally without reading raw law files manually.
2. Verify and publish immutable signed indexes and reports through the short public workflows.
3. Deploy at zero traffic and run readiness, MCP, assistant, route, accessibility and performance
   smokes.
4. Promote, verify production, restore the previous exact revision/manifest set, then restore the
   final candidate and record the rehearsal.
5. Review all public documentation and portfolio consumers, inventory local worktrees and delete
   only explicitly safe merged/generated artifacts.

Checkpoint: every specification release gate passes and the handoff records code, image, corpus,
indexes, models, reports, production revision and rollback evidence.

## Task discipline

- Start each behavior with a failing regression and observe the failure.
- Keep tasks to roughly five files; split cross-cutting work into contract, server and client commits.
- Never mix formatting, golden adoption, behavior and documentation in one commit.
- Review every Copilot comment and every P0/P1 adversarial finding before merge.
- Public evidence follows implementation and measurement, never the reverse.
- Do not activate hybrid retrieval, add a distributed quota service or redesign ingestion without a
  new failing gate and accepted decision.

## Main risks

| Risk | Mitigation |
|---|---|
| Partial corpus looks complete | Required publisher set, `/readyz` and population disclosure |
| Assistant invents success after a gap | Primary outcome precedence and deterministic reply owner |
| Model outage destroys usable result | Stream typed result first and preserve it on failure |
| Public MCP clients break | Internal adapter and frozen HTTP/stdio compatibility corpus |
| Evaluation signs a false green | Fail-closed schema, negative verifier tests and bound identities |
| UI churn hides regressions | Per-family goldens and real-browser route matrix |
| Long hosted build times out | Local resumable build plus short verified publication workflow |

## Definition of complete

Every box in `tasks/product-architecture.md` and section 12 of the specification passes, production
and rollback evidence is recorded, no P0/P1 remains, and public claims match the served system.
