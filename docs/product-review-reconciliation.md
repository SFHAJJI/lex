# Product architecture review reconciliation

Date: 2026-08-10

The two fresh-context reviews are inputs, not implementation authority. Every P0/P1 below is
classified against the accepted specification. `Accepted minimal` means the risk is real but the
reviewer's proposed machinery was reduced to the smallest contract that closes it.

## Independent review — initial findings

| Finding | Disposition | Accepted resolution |
|---|---|---|
| P0-1 primary selection undefined | Accepted | Closed `OperationPlan`; first authorized non-supporting legal operation is primary, section 5.1 |
| P0-2 Actions contradiction | Accepted | Constraint is no paid Actions; workflow states and public-runner policy, section 4.1 |
| P0-3 no deployment path | Accepted with different solution | Existing OIDC remains; local long build plus short public publication/deploy workflow, sections 4.1 and 9 |
| P1-1 incomplete tool mapping | Accepted | Total tool/action table, section 5.1 |
| P1-2 incomplete status mapping | Accepted | Shared legal and transport outcome table, section 5.2 |
| P1-3 finalizer failure and model budget | Accepted | Typed result survives, bounded retry/judge and numeric budgets, sections 5.3 and 8 |
| P1-4 workspace parity exceptions | Accepted | URL-addressable states plus named panel exceptions and pagination, section 6.5 |
| P1-5 reply locale | Accepted | Deterministic English/French policy and tests, sections 5.3 and 8.1 |
| P1-6 fail-open lazy guard | Accepted | No authority denies and release tests cover it, sections 5.4 and 12 |
| P1-7 model-reformulated authority contradiction | Accepted | Direct anchored evidence remains authorized; weak discovery remains denied, section 5.4 |
| P1-8 carry-over unspecified | Accepted | Most recent resolving turn, depth three, multi-work, reset rules, sections 5.4 and 5.5 |
| P1-9 weak evidence undefined | Accepted | Explicit strong/weak reason and anchor rule, section 5.4 |
| P1-10 New control undecided | Accepted | New conversation clears context but retains workspace, section 5.5 |
| P1-11 starter prompt gaps | Accepted | Every shipped starter is a contract and candidate smoke, section 8.1 |
| P1-12 clarification round trip | Accepted | Atomic value, one replay, non-selection and stale rules, section 5.4 |
| P1-13 replica quota multiplication | Accepted minimal | Pin one replica instead of adding distributed quota infrastructure, section 5.8 |
| P1-14 refusal list drift | Accepted | One shared status source; remove unimplemented status unless produced, sections 5.2 and 12 |
| P1-15 ingestion contracts unowned | Accepted | Enforced ingestion and promotion contract, section 7.1 and task 4.1 |
| P1-16 future evidence omitted | Accepted | Evidence context and provisional reply/workspace disclosure, section 5.7 |
| P1-17 benchmark chain migration | Accepted | Blind v3, signed regeneration and preserved v2 history, sections 8.3 and task 4.3 |
| P1-18 vacuous LU denominators | Accepted | Per-collection non-empty denominators and strata, section 7.2 |
| P1-19 leakage undefined | Accepted with explicit policy | Query separation is required; work overlap is measured and a tuning-unseen subset is added, section 7.2 |
| P1-20 activation numbers absent | Accepted | Fixed latency, memory, relevance and regression thresholds, section 7.2 |
| P1-21 route IA incomplete | Accepted | Complete source-controlled route ledger and fitness test, section 6.6 |
| P1-22 collision requirement vague | Accepted | Concrete 320 by 568 intersection and clearance rule, section 6.5 |
| P1-23 performance unmeasurable | Accepted | Pinned route budgets and assistant percentiles, section 8.4 |
| P1-24 browser venue absent | Accepted | Local fixture Playwright/axe plus candidate smoke, section 8.2 |
| P1-25 fixtures cannot reach cases | Accepted | Extended fixture is required before phase completion, section 8.1 |
| P1-26 stale/failed live eval not blocking | Accepted | Signed report must pass and match candidate identities, section 8.3 |
| P1-27 evaluator fails open | Accepted | Ungradeable and empty-context rows fail release mode, section 8.3 |
| P1-28 golden adoption unsafe | Accepted | Explicit update flag, family review and dirty-golden failure, sections 11 and 12 |
| P1-29 paused automation copy false | Accepted | Scheduler/freshness state is evidence-derived, sections 4.1 and 9 |
| P1-30 rollback unspecified | Accepted | Exact revision/manifest rollback and rehearsal, sections 7.1 and 12 |
| P1-31 signing Actions-bound | Accepted with different solution | Keep Key Vault OIDC and run short free public workflows; do not introduce local PEM signing, sections 4.1 and 9 |
| P1-32 inference cost unauthorized | Accepted | Fixed calls, token envelope, EUR 10 preflight ceiling and separate audit quota, section 8.3 |
| P1-33 non-advice boundary unowned | Accepted | Application-owned `legal_boundary` disposition and forbidden-claim cases, sections 5.2, 5.3 and 8.1 |

## Independent review — implementation findings

| Finding | Disposition | Accepted resolution |
|---|---|---|
| P0-01 partial corpus appears complete | Accepted | Required publisher readiness and scoped population disclosure, sections 7.1 and 12 |
| P0-02 deployment impossible | Accepted with public-OIDC solution | Sections 4.1 and 9, tasks 0.2 and 0.3 |
| P1-01 operation authority not total | Accepted | Sections 5.1 and 5.2 |
| P1-02 legal boundary not enforced | Accepted | Application-owned legal-boundary policy, sections 5.2, 5.3 and 8.1 |
| P1-03 temporal/provenance loss | Accepted minimal | Bounded evidence context, not a duplicate full MCP envelope, section 5.7 |
| P1-04 ingestion failure orphaned | Accepted | Section 7.1 and task 4.1 |
| P1-05 deterministic latency/cost impossible | Accepted | Versioned early result stream and numeric budgets, sections 5.6 and 8.4 |
| P1-06 conversation authority unclear | Accepted | Sections 5.4 and 5.5 |
| P1-07 quota not distributed/durable | Accepted trade-off | One replica and honest best-effort counter; Azure quota is hard control, section 5.8 |
| P1-08 public bounds incomplete | Accepted | Streamed byte bound and bounded MCP inputs/outputs, section 9 |
| P1-09 privacy/injection absent | Accepted | Data-flow disclosure, log allowlist and multi-channel canary tests, sections 5.5 and 9 |
| P1-10 signed false-green evaluation | Accepted | Fail-closed schema, frozen clock, identities and negative verifier tests, section 8.3 |
| P1-11 MCP migration unclear | Accepted minimal | Internal assistant adapter and frozen `7239f50` contract corpus, section 10 |
| P1-12 route gate not executable | Accepted | `docs/public-route-ledger.md` and endpoint fitness test, section 6.6 |

## Amended-spec review

| Finding | Disposition | Accepted resolution |
|---|---|---|
| P1 compound requests had only one authoritative outcome | Accepted | Freeze an ordered operation plan and give every requested operation an order-invariant authoritative outcome, sections 5.1 and 5.2 |
| P1 new public bounds conflicted with MCP compatibility | Accepted | Publish an explicit MCP 2.0 migration and derive HTTP, stdio and metadata versions from one source, sections 1 and 10 |
| P1 MCP overload behavior was not specified | Accepted | Bound executing, queued, nested hybrid and rolling-rate admission with deterministic errors, sections 9 and 12 |
| P1 latency excluded model planning | Accepted | Add per-round and total submit-to-result deadlines and report each latency segment, section 8.4 |
| P1 normative task references pointed to historical files | Accepted | Make the product-specific plan and checklist authoritative and label the older files historical, section 11 |
| P1 Gate 0 assumed a nonexistent staging environment | Accepted | Deploy a zero-traffic revision to the production app with `promote=false`, then promote and rehearse exact rollback explicitly, sections 4.1 and 12 |
| P1 planned operations incorrectly contained post-execution outcomes | Accepted | Separate immutable requested operations, pending executions and terminal operation results, sections 5.1 and 5.2 |
| P1 `Check the work` omitted the required About route | Accepted | Add About to the exhaustive disclosure menu while preserving `/about` in the route ledger, sections 6.1 and 6.6 |

## Deliberately deferred P2 work

P2 suggestions remain visible in the source reviews but do not block this release unless a failing
test or production measurement promotes them. Examples include strict CSP migration, a distributed
quota service, a full screen-reader matrix on multiple operating systems, and premature removal of
`lex-index/2`. Deferral prevents the correctness release from becoming an infrastructure rewrite.
