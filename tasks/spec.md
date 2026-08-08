# Spec: Retrieval, enrichment, and legal-research agent experiment

## Objective

Prove, before production implementation, a retrieval and assistant architecture for lawyers
researching Luxembourg and European Union law. The experiment must distinguish reliable
navigational search from exploratory legal research, preserve every byte and date of publisher
evidence, and select a clean Microsoft Agent Framework orchestration for clarification, tool use,
multi-turn research, and grounded answers at acceptable latency and cost.

The experiment is disposable. Its code and indexes are never production artifacts. Durable
outputs are benchmark cases, measurements, provenance-aware enrichment inputs, and architecture
decisions that can be reimplemented cleanly on a production branch.

## Product contract under test

### Deterministic search

- Resolve CELEX, ELI, ECLI, article identifiers, official titles, short titles, and reviewed
  professional aliases before ordinary provision ranking.
- Rank exact official identifiers first, reviewed aliases second, official titles third,
  article number/heading evidence fourth, provision keywords fifth, validated model-derived
  discovery metadata as a weak corroborated signal sixth, and semantic similarity seventh.
- Keep aliases in a compact work-level FTS index. Never repeat them in provision vectors.
- Keep keyword search deterministic. Hybrid adds the pinned local encoder and fixed fusion only.
- Use hierarchy, practice area, source class, legal form, legal status, language, and date as
  filters and documented tie-breakers, not invented legal facts.

### Enrichment workbench

- Materialize a disposable candidate database from the current signed index inventory and
  reconcile it against corpus/publisher evidence.
- Extract publisher fields and deterministic normalizations without an LLM.
- Use an offline LLM to derive work-level common names, acronyms, concise descriptions, legal
  concepts, search synonyms, and candidate practice-area assignments.
- Require source evidence, model/deployment, prompt hash, timestamp, and confidence for every
  proposal.
- Reject collisions, identifier-like inventions, invalid evidence anchors, unsupported
  classifications, and non-repeatable output automatically.
- Require publisher confirmation or review before a name/alias receives exact-name rank. Admit
  validated model-derived descriptions, concepts, and synonyms only to a separately labelled weak
  discovery field; they cannot become legal facts or filter truth.
- Export accepted additions and overrides with provenance. Publisher metadata is never duplicated
  into the durable configuration.
- Merge publisher metadata, reviewed aliases, and validated model-derived discovery fields during
  indexing. Test both compact work-level FTS alone and FTS plus one work discovery vector. Runtime
  never opens the workbench database.

### Authoritative-text invariants

- Enrichment cannot write publisher text, official titles, identifiers, dates, hierarchy,
  binding status, consolidation status, relationships, hashes, or occurrence mappings.
- Reading, citations, timelines, comparison, and diffs use only authoritative text blobs and
  occurrence mappings.
- Pre/post enrichment tests must prove identical authoritative SHA-256 inventories and identical
  representative read/compare output.
- The future signed manifest records legal-content and provenance-aware enrichment digests
  separately.

### AI assistant

- Exact named-work resolution runs deterministically before generative reasoning.
- Pure navigation can return a direct structured result without an LLM or judge.
- Descriptive questions use one Microsoft Agent Framework legal-research agent, not peer agents.
- Agent Framework is required. Compare two Agent Framework orchestration variants:
  1. typed plan, deterministic MCP execution, grounded synthesis;
  2. one tool-calling agent using compact adapters over the same `McpCore`.
- Both variants use bounded sessions, typed clarification, at most two search attempts, exact
  `as_of`/article validation before claims, and deterministic citation validation.
- Only synthesized legal prose runs through a conditional grounding judge. Direct publisher text,
  exact lists, and navigation skip the judge.
- A failed judgment permits one corrected draft; a second failure produces an honest refusal.

### Search results and facets

- Jurisdiction is the only top-level disjoint result partition.
- In the all-jurisdictions view, show Luxembourg and European Union sections, both expanded.
- Within each jurisdiction, group matching passages under their law instead of duplicating laws
  and passages in separate global lists.
- Practice area, hierarchy, source class, legal form, status, and language remain facets because
  they overlap or cut across one another.
- When a jurisdiction is selected, hide facets with fewer than two meaningful choices and show
  only values emitted for that jurisdiction. Changing jurisdiction clears incompatible values.
- The experiment must preserve URL-addressable filter state and keyboard/accessibility behavior.

## Tech stack

- .NET 10 and the repository's existing projects.
- SQLite/FTS5 and the pinned `intfloat/multilingual-e5-small` local encoder.
- Microsoft Agent Framework / `Microsoft.Agents.AI.OpenAI` 1.13.0, matching the existing sites'
  installed surface.
- Azure OpenAI `gpt-5-mini` through `DefaultAzureCredential`; no keys in source or output.
- React/TypeScript only for the isolated result-layout prototype.

## Commands

```powershell
dotnet build Lex.slnx --no-restore
dotnet test tests/Lex.Tests/Lex.Tests.csproj --no-restore
npm --prefix web test
npm --prefix web run build
```

Experiment-specific commands will be added under `experiments/retrieval-agent-v2/README.md` and
must accept explicit input/output paths. They may not silently read production mounts.

## Project structure

```text
experiments/retrieval-agent-v2/   Disposable harness, fixtures, reports, and candidate DB schema
tasks/spec.md                     This approved experiment contract
tasks/plan.md                     Dependency-ordered execution plan
tasks/todo.md                     Verifiable task checklist
src/ and web/src/                 Read-only until an experiment wins and a new production PR starts
```

## Code style

Prefer explicit records and small functions over framework wrappers or generic pipelines:

```csharp
public sealed record EnrichmentProposal(
    string Work,
    string Language,
    string Kind,
    string Value,
    string Evidence,
    string PromptHash);
```

Experiment adapters must call the existing transport-independent `McpCore`; they may compact
LLM-facing evidence but may not reimplement retrieval or legal time semantics.

## Testing strategy

- Failing unit tests first for index ownership, normalization, collision handling, ranking, and
  authoritative-text invariants.
- Real-data integration tests on a small corpus containing GDPR, its corrigendum, DORA, the AI
  Act, renewable-energy legislation, and selected Luxembourg works.
- Shadow tests on copies of the full local indexes; never mutate release artifacts.
- Three repeated Agent Framework runs per scenario while selecting an architecture; stochastic
  claims require all repetitions to pass.
- Snapshot/accessibility tests for result grouping and jurisdiction-scoped facets.
- Record code commit, corpus commit, index digest, model/deployment, machine, timestamps, latency,
  token counts, tool calls, retrieval ranks, judgments, and failures.

## Boundaries

### Always

- Copy real indexes before experiments.
- Preserve and compare authoritative hashes.
- Use scoped paths and redact credentials.
- Keep named navigation deterministic.
- Record failed runs as evidence.

### After gates pass

- Reimplement passing behavior cleanly on production branches; never merge the disposable harness
  wholesale.
- Rebuild, sign, publish, deploy, and live-test the full release indexes through the existing
  controlled rollout.
- Keep public API additions backward compatible and architecture status honest.

### Never

- Mutate or sign an experiment index as a release.
- Use unvalidated LLM metadata as authoritative metadata, public filter truth, or a strong rank
  signal.
- Let enrichment alter authoritative text or legal-effect metadata.
- Add a runtime metadata database or duplicate aliases into every provision vector.
- Present engineering classification as publisher-supplied metadata.

## Success criteria

- Exact identifiers and reviewed aliases return the intended base work first in keyword and hybrid
  modes in every test.
- Longer queries containing an exact alias retain that work constraint without losing provision
  evidence.
- Model-derived discovery metadata improves targeted descriptive recall without independently
  pinning a work or exceeding the two-percent unaffected-query regression gate.
- Representative authoritative text hashes and compare outputs are byte-identical before/after
  enrichment.
- Descriptive benchmark targets appear in the top three candidates in all three repetitions, or
  the scenario is honestly classified as a corpus gap.
- Expected ambiguity produces one typed question with two or three options; harmless ambiguity
  does not trigger a question.
- Temporal follow-ups retain the named work and use the requested historical date without leakage.
- Every generated legal claim has a validated work, publisher date, provision, and permalink;
  unsupported-claim count is zero after the conditional judge.
- Named navigation completes without an LLM. Warm descriptive turns record p50/p95 latency and
  tokens; the selected variant must beat the alternative on correctness first, then cost/latency.
- Search-result prototypes work at 320, 768, 1024, and 1440 pixels and remain keyboard usable.

## Not doing

- No full-corpus rebuild or Azure deployment during the experiment.
- No multi-agent hierarchy.
- No unvalidated LLM metadata as legal fact, public filter truth, or strong rank signal.
- No runtime sidecar metadata database.
- No practice-area result grouping.
- No claim that experiment judgments are lawyer-reviewed.

## Open questions resolved by evidence

- Whether typed plan/execution or direct tool-calling is more reliable and economical.
- Whether a work-level FTS table alone or FTS plus one work discovery vector provides the best
  relevance/latency/size balance.
- Which generated discovery fields and weak weights improve recall without regressing unaffected
  exact and keyword queries.
- Whether current publisher/scope metadata is sufficient for useful jurisdiction-specific facets.
- Which descriptive failures are retrieval/ranking defects versus genuine corpus gaps.
