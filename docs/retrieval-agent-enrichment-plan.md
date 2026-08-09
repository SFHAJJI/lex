# Search enrichment and legal-research agent

Status: scoped experiment passed; publisher-first production implementation is building and no unmerged behavior is claimed as shipped

Decision status: D75 building, D76 gated, D77 planned

Review status: engineer-reviewed experiment evidence and adversarial architecture review; lawyer review remains pending

## Why this program exists

Lex already preserves official Luxembourg and EU wording over time, but two different user needs
must not be confused:

1. A lawyer who knows a law, identifier, phrase or article needs deterministic retrieval.
2. A lawyer who can describe a problem but does not know the relevant instrument needs research
   assistance that plans, searches, verifies and cites evidence.

The search bar remains the deterministic front door. The assistant becomes an optional research
layer over the same read-only MCP tools. A generative model never becomes a search index, a source
of legal text, or a substitute for publisher evidence.

## Experiment decision, 2026-08-08

The disposable spike is recorded in draft PR #99 at commit `6c3ec59`. It selected a typed Agent
Framework plan followed by deterministic execution and validation for its frozen subscope. The direct
tool-calling comparator was rejected after a contract failure and after two of three photovoltaic
gap runs substituted Directive 2014/24/EU instead of reporting the corpus gap.

The selected run recorded zero deterministic or gap false positives across 18 planner runs and 21
turns. Its grounding suite recorded zero citation escapes, 10 judge passes, one repaired answer,
one evidence-limited refusal, six correctly reported gaps, three typed clarifications and a cold-
inclusive work-retrieval p95 of 168.60 ms. The immutable evidence identifiers are:

- work evaluation: `bdb037fc2eb8ba5e7075c974c5b80767cf6a915cd552011386cf38c27a41dcb5`;
- planner: `4c5b528224643b2f73c2e67f509e3c8cc55529f3d6eacaa4118efba7e7450258`;
- typed execution: `641c3332062f0159a898caf669cd164bae423ae059c18f4851e712632af2781e`;
- grounding: `9f74e5868a3faa3d9293137720d61da97e632cdd7cc9566dd4a1a90fd71b0c6e`;
- rejected direct comparator: `9410255cc12e30ab9cc142ca1f0050eefe7fcef5ad89b94bbb17a6df1785de55`.

These are engineer-reviewed experiment results, not lawyer-reviewed relevance judgments, a
full-corpus enrichment validation or production claims. Production code is reimplemented in small
PRs rather than merging the spike. Official publisher metadata and reviewed aliases ship first;
model-derived discovery remains inactive until a separate held-out ablation justifies it.

## Selected target architecture

```text
search bar
  -> exact identifier / reviewed name / official title resolution
  -> weak work-level discovery metadata recall
  -> provision keyword or optional local hybrid retrieval
  -> dated law and matching passages

AI assistant
  -> deterministic named-work pre-resolution
  -> understand intent and material ambiguity
  -> typed clarification, or one legal-research plan
  -> compact read-only MCP searches
  -> inspect candidates and validate work/date/article via as_of
  -> grounded prose
  -> deterministic citation validation
  -> conditional grounding judge
```

There is one Microsoft Agent Framework legal-research agent, not a hierarchy of peer agents.
Agent Framework is a fixed implementation boundary; the experiment selects its cleanest
orchestration, memory and tool contracts rather than deciding whether to use it. Exact navigation,
publisher text, timelines and rendered lists bypass generation and therefore bypass the judge.

## Enrichment workbench

The enrichment database is a disposable build workbench, not a runtime service.

1. Inventory the current signed index, then reconcile each work to authoritative corpus and
   publisher records.
2. Extract official titles, identifiers, document types, languages, publisher status, EUR-Lex
   directory/EuroVoc values and recorded relationships deterministically.
3. Let an offline LLM derive work-level discovery candidates: common names, acronyms, concise
   descriptions, legal concepts, search synonyms and candidate practice-area assignments. Every
   record carries its deployment, prompt/schema hash, timestamp, confidence and evidence anchors.
4. Reject collisions, identifier-like inventions, unsupported classifications, invalid evidence
   anchors and non-repeatable output automatically.
5. Route names and aliases through publisher confirmation or review before they can receive an
   exact-name rank. Admit only validated model-derived descriptions, concepts and synonyms to a
   separately labelled weak discovery field; they never become legal facts or filter truth.
6. Export additions and overrides with their provenance. Do not duplicate publisher metadata into
   the reviewed configuration.
7. Merge publisher metadata, reviewed aliases and validated model-derived discovery fields during
   indexing. Build one compact work-level FTS record, one base work vector and separate quarantined
   vectors for each accepted discovery concept. Append those typed records to the same
   vector artifact as provision chunks and map their disjoint ordinal range in SQLite. Record the
   enrichment digest and mixed-vector layout in the artifact stamp and signed manifest. Runtime
   opens only the rebuilt signed index and its one verified vector artifact. Ordinary production
   retrieval ignores quarantined concept fields until their independent graduation gate passes.

The LLM may publish validated discovery aids into the signed index; it never publishes legal
evidence. Legal text, official titles, identifiers, dates, hierarchy, binding status,
consolidation status and relationships cannot be written by this path. Model-derived practice
areas remain search hints until separately approved for use as public filter metadata.

## Byte-identity boundary

Enrichment changes discovery metadata, never authoritative law.

- Publisher UTF-8 text blobs and SHA-256 identities remain unchanged.
- Work/version/anchor occurrences continue to map to the same text states.
- Reading, citation, timeline, comparison and diff code never reads enrichment as legal wording.
- The manifest records legal-content and reviewed-enrichment digests separately.
- Pre/post tests compare the complete authoritative hash inventory and representative structured
  reads and diffs byte for byte.
- Public provenance distinguishes publisher metadata, deterministic Lex normalization, reviewed
  aliases and model-derived discovery aids.

A bad reviewed label could still harm retrieval or filtering, but it cannot change what a law says.
Ranking and classification therefore have separate quality gates from byte identity.

## Ranking safety belt

Lex does not assign one global weight to every enriched field. It uses match tiers:

1. exact official identifier;
2. exact, reviewed, collision-free professional alias;
3. official title or short title;
4. article number and heading;
5. exact provision wording;
6. validated model-derived subject/search enrichment as a weak recall signal;
7. semantic similarity.

An exact alias such as `RGPD` may pin one unambiguous work. A broad subject phrase such as
`protection des données` may eventually nominate a work as a weak discovery result but never becomes
legal evidence. Exact identifiers and reviewed aliases remain deterministic. Publisher work
semantics may nominate candidates; model-derived keyword and concept-vector fields are currently
quarantined from ordinary retrieval. They can receive a bounded lower weight only after an
independent holdout proves that they improve residual discovery without reversing direct provision
evidence or creating weak-only assistant selection. Rejected or unvalidated proposals have no
weight because they are absent from the index. Identifiers and aliases are never fuzzy-expanded.
Ambiguous aliases cannot pin a work.

## Single vector artifact decision

The experiment used a separate work-vector sidecar to isolate the spike. Production deliberately
does not copy that deployment detail. Provision, base-work and accepted-concept embeddings share
one immutable `lex-vectors/1` artifact, while SQLite records each vector's typed ordinal mapping.
Provision ordinals remain a contiguous first range and work ordinals a contiguous second range;
the reader verifies complete, non-overlapping coverage before serving hybrid search.

Every semantic build creates base-work vectors from publisher titles and facets. Supplying a
reviewed enrichment artifact may add quarantined aliases and concept records but is not required to
make official work metadata searchable. This prevents deployment configuration from silently
disabling the publisher-metadata layer and keeps weak activation independently reversible.

Each embedding still stores a binary sign code for the fast candidate scan and an int8 code for
reranking. Those are two compact encodings of the same model output, not two embeddings. One query
embedding is reused for both provision and work scans. This keeps the experiment's measured ranking
shape while removing a second Azure artifact, manifest entry and mount/failure boundary.

Domains and hierarchy remain primarily filters. If the assistant infers a domain rather than the
user selecting it, the experiment must compare scoped and unscoped candidates before excluding
law.

## Agent Framework experiment

Microsoft Agent Framework is required because it supplies a standard host for typed tools,
structured turns and sessions; it does not make legal reasoning correct automatically. Two clean
single-agent Agent Framework variants compete on the same scenarios and `McpCore` evidence:

### Variant A: typed plan and deterministic execution

The model emits intent, jurisdiction, date/range, material ambiguity, two to four compact queries
and tool choices. Application code executes the plan through MCP, validates candidates, then asks
for a grounded synthesis.

### Variant B: direct tool-calling agent

One agent receives compact read-only adapters over the same MCP tools, may reformulate at most
twice, and must call `as_of` or exact article retrieval before making legal claims.

Named-law resolution is deterministic in both variants. Pure navigation returns without an LLM.
This prevents a stochastic agent from asking why a user wants the RGPD or claiming that a mounted
work is unavailable.

## Clarification and memory

Material ambiguity returns a typed turn:

```json
{
  "type": "clarify",
  "question": "Which support mechanism do you mean?",
  "options": [
    "Public procurement",
    "Renewable-energy auctions",
    "Both"
  ]
}
```

The workspace renders accessible buttons and keeps a free-text answer. Missing `today` is not a
reason to ask; the assistant may use today and disclose the assumption. Jurisdiction, legal
mechanism or historical period should be clarified only when different answers would result.

Conversation memory is bounded and restorable. A Container App restart or scale-to-zero event must
not silently change a follow-up into an unrelated first question. The experiment will compare
Agent Framework session serialization with a short client-restored history and record the chosen
retention/privacy boundary. Durable personal profiling is out of scope.

Accepted, signed work cards are the durable reusable output of offline enrichment. They may improve
both deterministic search and future assistant planning. Provider prompt caching may reduce the
cost of a repeated stable prompt prefix, but it is ephemeral: it is never conversation memory,
legal evidence, an enrichment store or a deployment dependency.

## Grounding gate

Deterministic validation checks that each cited permalink resolves to the returned work, publisher
date and anchor. The conditional judge then checks synthesized prose only:

- every factual legal claim is supported by returned evidence;
- the answer addresses the user's actual question;
- work, date and provision citations are correct;
- no unsupported legal interpretation was added.

One failed judgment permits one corrected draft. A second failure returns an evidence-limited
refusal. Direct evidence views do not pay judge latency or token cost.

## Result and facet design

Jurisdiction is the only top-level disjoint partition. In an all-scope search, Luxembourg and EU
sections are both visible. Within each section, matching passages nest under their law, avoiding a
duplicate global `Laws` list and `Where it is said` list.

Practice area can be multi-valued. Hierarchy, source class, legal form, legal status and language
are orthogonal. They remain facets rather than result groups.

When a jurisdiction is selected, the UI shows only values emitted by that jurisdiction and hides
empty or single-choice facets. In all-scope mode, values are marked or grouped by applicability.
Changing jurisdiction clears incompatible dependent filters. Counts, loading, empty and error
states remain explicit, and filter state stays URL-addressable.

## Experiment scenarios and evidence

The clean spike uses a small real corpus and copies of the full local indexes. It covers:

- CELEX/ELI/ECLI, official names, `RGPD`/`GDPR`, `DORA`, `AI Act`, titles and articles;
- data-breach notification, ICT third-party risk and photovoltaic procurement/support;
- corrigenda, amending acts and unrelated semantic neighbours;
- material ambiguity and harmless missing context;
- dated follow-ups and comparison questions;
- unavailable text, unknown work and genuine corpus gaps.

Every stochastic scenario runs three times while the architecture is selected. Reports record code
and corpus commits, index/model hashes, machine/deployment, retrieval ranks, tool calls, latency,
tokens, result size, citations, judgments and failures.

## Gates

The plan can graduate only when:

- exact identifiers and reviewed aliases return the intended base work first in keyword and hybrid;
- longer questions containing an exact alias retain that work without suppressing provision search;
- authoritative hash inventories and representative reads/diffs are identical before and after;
- descriptive targets rank in the top three in every repeated case, or the result is correctly
  identified as a corpus gap;
- expected ambiguity yields one typed question and harmless ambiguity does not;
- temporal follow-ups retain the work and requested date with zero leakage;
- no unsupported legal claim survives deterministic validation and the conditional judge;
- ordinary unaffected retrieval regresses no more than the existing two-percent program gate;
- latency, token and Azure-cost measurements justify the selected agent variant.

Until those gates pass, keyword remains the default, the current assistant architecture remains
live, and the Next Architecture page must label this program as building. Failure means iterate on
the Agent Framework orchestration or enrichment contract, not fall back to an unstructured chat
implementation.

## Alternatives rejected

- Runtime sidecar metadata database: duplicates truth and can drift from signed artifacts.
- Unvalidated LLM metadata used as authoritative metadata or a strong rank signal: nondeterministic
  discovery hints must remain provenance-labelled, bounded and regression-gated.
- Aliases embedded into every provision vector: repeats work metadata and contaminates meaning.
- One universal enrichment weight: broad subjects can overwhelm exact legal evidence.
- Multi-agent hierarchy: adds latency, tokens and failure modes without an independent authority.
- Chatbot-first navigation: hides date, source, jurisdiction and comparison context.
- Grouping results by practice area, form or language: overlapping dimensions duplicate works.

## Delivery after validation

The experiment passed, so production work is being reimplemented in small reviewed PRs:

1. provenance-aware enrichment contract, offline LLM workbench and work-level FTS;
2. ranking and authoritative-identity gates;
3. deterministic subject resolver and typed assistant contract;
4. selected Microsoft Agent Framework execution, bounded memory and conditional judge;
5. jurisdiction-first result/facet UX;
6. full signed index rebuild, public benchmarks, candidate deployment and controlled promotion.

The disposable experiment branch is never merged wholesale.
