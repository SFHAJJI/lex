# Limits and scale

Scale decisions are tied to observable triggers. The system stays simple while the current
bottleneck is bounded, and each next move names the capability it buys and the complexity it adds.

The public assistant is currently unavailable. Assistant and agent limits below are dormant target
V3 controls, not evidence of live model traffic.

![Current single-process Lex scales through measured triggers: narrow lock scope first, externalize ledgers before replicas, then move artifacts to local disk only when memory requires it.](/built/diagrams/scale.svg)

[Open the scale diagram at full size](/built/diagrams/scale.svg)

| Box | Responsibility | Owner |
|---|---|---|
| One process | Keep local indexes authoritative; keep dormant assistant state isolated until V3 activation | `Lex.Web` with in-process `Lex.Mcp` and contained `Lex.Ask` |
| Narrow gates | Relieve measured encoder or vector contention before adding services | MCP and future assistant admission controllers |
| Externalize state | Make quotas, idempotency and thread continuity replica-safe | deferred shared-state boundary |
| Replicas or local disk | Add request capacity only after state is shared; move artifacts only after memory pressure | future runtime and deployment decision |

## Current boundaries

| Boundary | Current design | Consequence |
|---|---|---|
| Runtime | One always-on Container Apps replica with local immutable indexes | Fast in-process calls; process-local quotas and thread memory are authoritative |
| Assistant, dormant | Target defaults of four concurrent turns, 200 accepted turns per client address and 400 globally per day | No public turns are admitted during V3 replacement; limits require fresh evidence before activation |
| MCP | Eight executing, sixteen queued, a two-second queue deadline, two hybrid slots, and rolling 120 per client and 600 global calls per minute | Overload becomes a typed refusal rather than unbounded latency |
| Agent, dormant | Target bounds of eight frozen operations, 64 evidence items and 96,000 evidence characters | These limits authorize nothing until the V3 assistant is promoted |
| Retrieval | Keyword default; hybrid available only behind signed evidence gates | Conceptual recall improvements do not outrank measured precision |
| Release | One current revision, one exact rollback, one transient candidate | No unlimited artifact or revision accumulation |

Fresh v4 relevance, latency, memory, coverage and cold-start measurements are pending the exact
candidate promotion. Older figures are historical observations and are not presented as current.

## Triggered next moves

| Observable trigger | Next move | Cost introduced |
|---|---|---|
| MCP queue-deadline refusals | Narrow the gate to encoder and vector work before adding infrastructure | More concurrency paths to test |
| After V3 activation, global daily cap saturation or repeated Azure OpenAI 429s | Add an alert, then raise model quota and public budget together; consider provisioned throughput only after utilization proves it | Higher fixed or variable model cost |
| Sustained served p95 above the release threshold | Externalize quota, idempotency and thread state, then add replicas | A shared state dependency and distributed coordination |
| Working set approaches the container memory gate | Move signed artifacts to verified VM-local disk under D55 | A second deployment path and OS operations |
| Third publisher admitted | Make the required-publisher set one source and remeasure fan-out | More vocabulary and latency variance |

## Known limitations that matter to a reader

- Some official states contain no safely extractable wording; they remain explicit gaps.
- Provision extraction quality has a historical empty-text backlog. The additive Memorial v2
  profile is shipped in code, while the refreshed corpus measurement waits for the v4 ingestion.
- EU citation edges and amendment relations are not yet a complete query surface.
- MCP comparison identifies changed states but does not yet return every provision-level text diff.
- Production diagnostics are sanitized but not all are correlated end to end by request id.
- Shared ACR cleanup remains inventory-only until registry ownership is isolated or audited.

The detailed and dated backlog lives in [known defects on GitHub](https://github.com/SFHAJJI/lex/blob/main/docs/known-defects.md).
Deliberately absent: generated consolidation, model-derived legal identity, silent taxonomy merging,
unbounded replanning and a framework rewrite.
