# ADR: identity-based release retention

Status: accepted for implementation, cleanup execution remains operator-controlled.

## Context

Lex needs one deployable production release, one deployable rollback release, and a transient
zero-traffic candidate while release gates run. Retention based only on age can delete an image
or artifact that an Azure revision or audited release-state receipt still identifies.

Azure Container Apps revisions are immutable. Azure tracks inactive revisions separately from
active revisions and purges the oldest inactive records after `maxInactiveRevisions` is exceeded.
The supported CLI exposes activate, deactivate, list, show and restart operations, but no command
to delete one revision record directly.

The Container Apps revisions documentation still labels configuring the maximum inactive
revision count as preview, even though the current stable Container Apps ARM schema exposes
`maxInactiveRevisions`. This workflow therefore treats the setting's documented oldest-inactive
purge behavior and exact post-write read-back as an explicit preview dependency, not an
unconditional platform guarantee.

Sources:

- https://learn.microsoft.com/azure/container-apps/revisions
- https://learn.microsoft.com/azure/container-apps/revisions-manage
- https://learn.microsoft.com/azure/templates/microsoft.app/2025-01-01/containerapps
- https://learn.microsoft.com/rest/api/resource-manager/containerapps/container-apps/update
- https://learn.microsoft.com/cli/azure/containerapp/revision
- https://learn.microsoft.com/cli/azure/acr/repository
- https://docs.github.com/rest/deployments/deployments#list-deployments
- https://docs.github.com/rest/deployments/statuses#list-deployment-statuses

## Decision

Use Azure's native inactive-revision limit and make the count match the release state:

- Steady state has current production active and exactly one inactive rollback under
  `maxInactiveRevisions=1`.
- Candidate publication first refuses to proceed unless that steady state is exact. It then sets
  the limit to 2, creates one candidate, runs its deployment gates, and deactivates it. Both
  success and failure/cancellation reconcile and read back the bounded waiting state: current
  production active, the prior rollback inactive, and at most one candidate inactive. A second
  deployment is refused while that candidate remains unresolved.
- Normal promotion proves the strict creation order `B < A < C`: prior rollback B, current
  evaluated production A, and exact evaluated candidate C. It activates C under the two-record
  waiting state, passes the signed and live gates, lowers the inactive limit to 1, switches C to
  100 percent, and deactivates A. Azure purges older B and retains exact A. The final state is C
  active with exact previously evaluated A as the sole inactive rollback. No per-promotion clone
  or equivalence claim is created.
- A receipt or status failure after the public switch activates exact A, routes it at
  100 percent, deactivates failed C, and proves the exact inverse steady state under
  `maxInactiveRevisions=1`. The workflow records a failed or unreceipted promotion that requires
  operator reconciliation. If a receipt deployment was created, every converged recovery path
  posts a failure status with bounded retries and reads the latest status back as `failure`; an
  unconfirmed invalidation fails closed instead of leaving the receipt silently reusable.
  Recovery authority is emitted only after Azure reports C as the sole traffic bearer;
  pre-switch recovery never writes traffic based on stale outputs.
- Rollback is a separate state path, not promotion with different labels. It starts from current
  production active and its pinned rollback inactive under `maxInactiveRevisions=1`. Activating
  the target leaves no inactive revision. A successful rollback switches to that exact target and
  deactivates former production, which becomes the sole inactive rollback. The target's existing
  signed assistant evaluation remains revision-bound; the prior successful release-state receipt
  proves that exact revision and image were retained for current production. Success leaves the
  rollback active and exact former production as the sole inactive revision.
- Every successful ordinary transition emits `lex-release-state-receipt/3`. Its
  `target_authorization` binds the new production revision to the evidence release that actually
  authorizes that revision; its `rollback_authorization` carries the former production revision's
  separate authority. The next transition consumes the newest successful receipt, verifies the
  live current image, selects the target's own authority, and swaps the former current authority
  into the new rollback slot. Thus a rollback never tries to authorize B with C's evaluation, and
  a C-to-R bootstrap exception followed by R-to-C preserves the two distinct authority chains.
- The caller-supplied prior deployment ID is confirmation, not ledger authority. Before candidate
  activation, the workflow scans at most five 100-record GitHub deployment pages for production
  `lex-revision-promotion` records, queries each current status, validates strict newest-first
  `(created_at, id)` order and selects the first success. The supplied ID must equal that derived
  head, its receipt must normalize to the requested current/target transition, and the head is
  read again immediately before activation and immediately before public traffic mutation. A
  scan with no success inside the 500-record bound fails closed for operator reconciliation.
- A first-release source is authenticated whenever either the target authority or the new
  rollback authority carries it, not only during C-to-R fallback traffic. The source deployment
  must still be successful; its domain-separated OIDC receipt, complete package digest, artifact
  signatures, exact C evaluation and historical A/R/C equivalence manifest are all verified.
  Historical mode relaxes only age and old-A live presence. It never relaxes repository/root,
  tenant/subscription/app/revision identities, template/image/case/code digests or signature.

The one-time first official release uses a separate bootstrap because legacy failed candidates can
be newer than the old production revision:

- `.github/workflows/bootstrap-legacy-inventory.yml` first records every live revision and full
  template with `revision list --all`. The strict planner requires exactly one active A at 100
  percent, every other revision inactive at zero traffic, explicit UTC creation times and strict
  JSON types. A is digest-pinned; historical inactive Lex ACR image references and their complete
  templates are exact-fingerprinted and are never activated. The inventory performs no mutation.
- `.github/workflows/bootstrap-legacy-cleanup.yml` accepts only the successful exact-commit dry-run,
  independently reviewed plan SHA-256 and the literal one-time confirmation. It re-reads and
  compares the complete plan immediately before its only Azure mutation: setting
  `maxInactiveRevisions=1`. It never changes traffic or activation. Azure accepted that
  configuration before its inactive-revision list converged, so the workflow refuses a receipt
  until read-back proves exact A at 100 percent plus one reviewed inactive record.
- If an exact reviewed A+2 state remains after that configuration write, the same inventory may
  additionally authorize one direct, non-retried deactivation POST for the exact older inactive
  record. Microsoft documents deactivation, but not already-inactive retention reconciliation;
  this is therefore a bounded observed recovery, not a claimed platform guarantee. Three
  consecutive exact A+one-reviewed-survivor reads issue the ordinary cleanup receipt. An unchanged
  A+2 state is recorded as inconclusive and fails without another mutation.
- The bootstrap deploy requires that cleanup receipt. It builds immutable image I, creates active
  zero-traffic fallback R first from the intended canonical template, then deactivates R under
  max=1. This replaces the last legacy inactive record. It creates active zero-traffic C second.
  The only preparation state is therefore A active/100, R inactive/0 and C active/0, with exact
  chronology `A < R < C`; there is never a temporary third active revision process.
- Evaluate exact C. A separately signed `lex-first-release-equivalence/1` artifact binds A, R and C
  resource identities and states, strict `A < R < C` chronology, I, and the canonical R/C template
  digest. The digest excludes only `revisionSuffix`; code, artifact set, model configuration,
  environment, evaluation-catalog digest and 1-vCPU/2-GiB single-replica scale remain bound. R is
  labelled `equivalent_first_release_fallback`, never independently evaluated production. The
  signed promotion window expires after two hours.
- C's revision FQDN is directly reachable during that bounded evaluation window even though the
  application URL sends it zero traffic. A and C run separate in-process limiters, so the window
  is an explicit temporary exposure boundary: no public application traffic may target C, the
  exact evaluation must start immediately, and expiration requires the exact-confirmation
  `.github/workflows/bootstrap-abandon.yml` path. That path accepts only A active/100, R inactive/0,
  C active/0 with `A < R < C`, deactivates only C, and proves A active/100 plus C inactive/0. It is
  idempotent after cancellation.
- `.github/workflows/bootstrap-inventory.yml` then performs a mutation-free read-back of the exact
  three-revision state, immutable ACR digests and staging ETags. Its reviewed plan hash and
  exact-commit run are inputs to `.github/workflows/bootstrap-release-state.yml`, which rebuilds
  the complete plan before signed verification and immediately before traffic mutation.
- Success switches C to 100 percent and deactivates A under max=1. Because `A < R < C`, Azure
  purges A and retains exact R. Success ends with C active and R sole inactive on one immutable
  image digest. A failure before durable mutation authority performs no Azure writes. Once
  mutation is authorized, a pre-switch failure deactivates C, intentionally retaining C as the
  sole inactive retry record and leaving A at 100 percent; a fresh attempt creates a new R first.
  After A is deliberately purged, a receipt failure restores signed R active with C sole inactive
  and explicitly records that old A is no longer recoverable.
- The successful bootstrap deployment receipt is domain-separated as
  `lex-first-release-receipt/1` / `authorize-equivalent-first-release-fallback`. It is issued only
  after exact public C read-back and binds the tenant, subscription, Container App and C/R resource
  IDs, image/template digests, evaluation release, case/report/manifests, the complete signed
  package digest and reviewed cleanup plan. A later C-to-R emergency rollback does not pretend R
  has an ordinary C-bound evaluation. It verifies the exact still-live evaluated C release, the
  signed historical A/R/C equivalence package, the successful bootstrap receipt, and a fresh
  complete C-active/100 plus R-active/0 inventory immediately before moving traffic.
- If that exceptional rollback is used, R is older than the now-inactive C. Direct promotion from
  R is intentionally refused by the normal `B < A < C` chronology gate because Azure would purge
  R rather than retain exact current production. Use the successful rollback receipt to move
  forward to exact evaluated C first (the symmetric rollback path accepts either creation order),
  or independently evaluate R under a separately reviewed procedure. Once C is active with R sole
  inactive, normal exact-prior-release promotion resumes. This is a one-time recovery exception,
  not a recurring clone-equivalence mechanism.

An unresolved normal candidate C is newer than prior rollback B. Azure offers no per-revision
delete, so lowering the limit from 2 to 1 would purge B and retain failed C. Candidate abandonment
therefore cannot honestly be a one-step clone-and-health workflow. Until a newer replacement B'
has its own signed exact evaluation (or a verifier-supported canonical equivalence chain), the
bounded state remains A active with B and C inactive under limit 2 and new deployment is refused.

ACR cleanup is planned from immutable SHA-256 digests, never tags or age. The allowlist contains
exactly the live production and rollback image digests, plus the candidate digest while one is
being evaluated. The newest successful release-state receipt must bind those live production and
rollback identities exactly; older receipts remain audit evidence but do not retain deployable
images. A mismatch refuses cleanup. Deleting a manifest digest also removes every tag that
references that manifest, which is why tag-based protection is rejected.

Private prebuilt staging blobs are deleted by exact path and ETag only after the signed immutable
Blob release and public GitHub release have both been downloaded and hash-verified. Paths under
`releases/` are not cleanup candidates. A bootstrap inventory is only a dry-run allowlist: it
cannot delete staging owned by a concurrent lex-ops publication.

`.github/workflows/retention-inventory.yml` is read-only. It inventories Azure revisions, ACR
manifests and audited release-state receipts, then emits an exact dry-run plan through
`scripts/deploy/retention_plan.py`. It contains no deletion command.
The planner refuses to authorize ACR cleanup until at least one successful release-state receipt
exists and the newest receipt matches the live production and rollback revisions and digests.

Every mutation is preceded by a fresh identity/template/image/creation-time read and followed by
an exact state read-back. Automated pruning and traffic promotion must be disabled if Azure stops
exposing `maxInactiveRevisions`, changes the oldest-inactive purge rule, removes the preview
feature, or cannot return the exact bounded state asserted here. The reversal is inventory-only,
fail-closed operation followed by an operator-reviewed migration to a currently supported Azure
retention mechanism. It is never emulated by setting 0 inactive revisions or by widening the
limit to 100.

## Rejected alternatives

- Keep 2 inactive revisions after the gate closes. Two is only the bounded waiting-state limit;
  successful promotion returns to one inactive rollback.
- Temporarily use the default 100 inactive revisions. The supported state machine uses bounded
  limits 1 and 2; a broad retention window would hide rather than bound unexpected state.
- Clone current production on every promotion. A new Azure revision is not the exact evaluated
  revision, and matching only its image or template cannot transfer signed evaluation authority.
- Set the inactive revision limit to 0. The published ARM/Bicep contract has a minimum of 1.
- Delete one revision directly. The supported Container Apps revision API exposes activation,
  deactivation, listing, showing and restart, but no per-revision DELETE operation.
- Delete images or revisions after a fixed number of days. Age does not prove that an identity is
  unreferenced.
- Delete by ACR tag. Tags are mutable and deleting one manifest digest removes all of its tags.
- Delete arbitrary Blob prefixes. Only the two hash-pinned staging inputs owned by one successful
  publication may be deleted.

## Security boundary

The shared ACR currently has its admin account enabled. Lex itself uses managed identity for build
and pull, but the registry is shared with other applications. Disabling the admin account without
auditing those consumers could break deployments outside this repository, so that change is a
separate cross-application migration, not part of this retention change.

Broad automated deletion is therefore rejected under the current shared, legacy-RBAC boundary.
The target is either a dedicated Lex registry with an independently auditable lifecycle, or an
audited migration of every shared consumer to repository-scoped ABAC roles and managed identities.
Only after one of those boundaries proves exclusive digest ownership may the read-only retention
plan become an automated manifest-deletion workflow.

Likewise, bootstrap ACR digest deletion is blocked until all Container Apps revisions, App Service
container settings, AKS workloads, external pull identities and ACR access logs are audited for
the exact digests. A digest appearing only in Lex's revision inventory is a generic cleanup
candidate, not proof of an exploit or proof that no other application pulls it. Staging deletion
requires the lex-ops release/run ownership audit because GitHub and Azure identities cross the
public repository boundary.

The following are read-only inventory blockers, not authorization to change shared production
resources:

- **ACR admin account.** An enabled admin account is a generic credential risk, not by itself an
  exploit. A concrete path requires valid admin credentials to be exposed to an actor who can
  reach the registry. Before disabling or rotating it, inventory every Container Apps, App
  Service, AKS, automation and external pull consumer; identify each credential owner; and review
  ACR authentication/pull logs so every consumer has a tested managed-identity replacement.
- **Production GitHub authority.** Missing environment reviewers or branch protection is a
  generic governance finding. A concrete release path exists only if an untrusted actor can
  modify or dispatch an authorized workflow/ref and obtain production environment secrets or
  OIDC without independent approval. Before changing the boundary, read back the `production`
  environment's deployment branches, required reviewers and self-review rule; repository
  rulesets/branch protection; Actions and fork-approval policy; workflow token permissions; and
  every action pin and secret/variable consumer in both Lex and public lex-ops. Both repositories
  serialize their own `lex-production` jobs, but GitHub concurrency groups are repository-scoped;
  the environment review must also prove cross-repository operator sequencing before either side
  is allowed to mutate the shared Container App.
- **Azure OpenAI local keys and public network.** Either setting expands exposure but is not an
  exploit without a valid key or an independently exploitable network-facing service. Before
  disabling local authentication or public access, inventory every Lex and cross-application
  runtime, evaluator, grader, health check and deployment slot; prove managed-identity RBAC,
  private endpoints and DNS for each; then review resource authentication and network logs for
  unknown consumers.
- **Subscription-scoped Contributor.** Broad Contributor scope increases blast radius, but an
  exploit path requires compromise or misuse of that principal/token. Before down-scoping it,
  enumerate direct and inherited assignments, Azure Activity Log actions, all workflow commands
  and every shared resource/provider touched by the principal; derive and test the least-privilege
  roles across all consuming applications before removing the subscription assignment.
- **Public lex-ops secret boundary.** Public source does not reveal GitHub secrets by itself. A
  concrete path requires untrusted workflow code, an unapproved ref/fork, or a compromised action
  to reach the protected environment, OIDC token or secret. Hardening requires the cross-repo
  environment/ruleset/fork/action-pin audit above plus an exact inventory of which release,
  Key Vault and Azure variables are secrets versus public configuration.

Until those audits are recorded, shared ACR/Blob cleanup and live RBAC, registry, Azure OpenAI or
GitHub-authority hardening remain blocked. The workflows in this change only produce identity-
and-digest-bound plans and perform release-state mutations after their explicit gates; they do not
silently broaden a cleanup plan into shared-resource hardening.
