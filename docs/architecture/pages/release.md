# Release

Code, legal data, retrieval artifacts and assistant behavior are one release unit. A deployment is
not production merely because a container started.

![A signed artifact set becomes a zero-traffic candidate, passes health, retrieval, assistant and browser gates, then a separate signed promotion moves traffic while retaining the exact former production revision.](/built/diagrams/release.svg)

[Open the release diagram at full size](/built/diagrams/release.svg)

| Box | Responsibility | Lives in |
|---|---|---|
| Signed artifacts | Bind code, indexes, vectors, encoder and scope to exact hashes | local builder plus bounded `lex-ops` publication |
| Candidate | Start an immutable revision while production traffic remains unchanged | `deploy.yml` and Azure Container Apps |
| Independent gates | Verify readiness, retrieval, assistant behavior, browser behavior and human review | candidate scripts, signed evaluation release and protected environments |
| Promote or restore | Revalidate identities before one traffic change or exact rollback | `revision-traffic.yml` and Key Vault receipts |
| Retention boundary | Preserve production and one rollback without deleting unproven shared artifacts | Container Apps revision policy and `lex-ops` Blob cleanup |

## State machine

| State | Active production | Inactive records | Allowed next action |
|---|---|---|---|
| Steady | Current revision at 100 percent | Exact former production as rollback | Create one candidate |
| Waiting | Current revision at 100 percent | Rollback plus one zero-traffic candidate | Evaluate, promote or reconcile |
| Promoting | Evaluated candidate activated at zero traffic | Previous authorities still pinned | Revalidate evidence, then move traffic |
| New steady | New production at 100 percent | Exact former production as rollback | Emit and verify release-state receipt |

`maxInactiveRevisions` is one in steady state and two only while a candidate waits. Azure Container
Apps has no supported per-revision delete. An unresolved newer candidate cannot be discarded by
lowering the limit without risking the older valid rollback, so cleanup follows identity and
creation order rather than age.

## Evidence before traffic

The deploy workflow builds an immutable image from the exact code commit and signed artifact set,
creates a zero-traffic revision, and checks readiness, MCP, Luxembourg and EU search. The assistant
evaluation binds frozen cases, exact candidate, tool outcomes, injection canaries, latency and cost
to a signed report. Human review uses a separate signing authority. Only the traffic workflow can
verify those records and promote. No automated actor can approve its own release.

The current v4 release is still gated. New production measurements and claims of live behavior
belong here only after candidate evaluation and promotion produce the matching signed receipts.

## Container registry and Blob retention

The Azure Container Registry is shared and still has a legacy RBAC boundary. Broad digest deletion
is rejected because Lex's revision inventory cannot prove that another application does not pull a
digest. The clean target is either a dedicated Lex registry or an audited migration of every shared
consumer to repository-scoped ABAC roles and managed identities. Only then may an exact digest
allowlist become an automated deletion plan.

Private prebuilt staging blobs have a narrower ownership proof. The publisher may delete only the
exact staging paths it created, with matching ETags, after both the immutable Blob release and the
public release have been downloaded and hash-verified. Release paths are never cleanup candidates.
