# Lex hybrid retrieval and EU temporal expansion

Status: accepted, implementation in progress

Program version: `hybrid-eu/1`

Public review status: engineer-reviewed

## Product invariant

Lex answers what an official rule said on a requested date. Corpus expansion, storage
deduplication and retrieval improvements must preserve every official dated expression, exact
provision text, hash, anchor, timeline and comparison result. No component may manufacture a
consolidated legal text.

The structured MCP JSON and `UiEffect` contract remain the interface between retrieval, the
assistant and the workspace. Semantic chunks are retrieval aids only. They are never rendered
or compared as authoritative law.

## Delivery

1. Publish separate current, next, decisions and benchmarks surfaces.
2. Sign a whole-artifact manifest and automate verified deployment through Azure OIDC.
3. Replace the hard-coded EU shelf with reviewed domain configuration and bounded legal-history
   closure, retaining French and English versions.
4. Build `lex-index/3` with content-addressed text, occurrence mappings and transparent
   decompression.
5. Add local multilingual embeddings, deterministic rank fusion and controlled fuzzy fallback.
6. Enable hybrid retrieval only after the public relevance, temporal, latency and memory gates
   pass.

The machine-readable milestone and decision registry is `docs/architecture-program.json`. The
site renders that registry so a change of status has one source of truth.

## Corpus policy

A configured domain determines which works enter the corpus. It never determines which versions
of an accepted work survive. Every available official consolidated French and English expression
is retained, including a single-version unamended work. Original acts, amendments, corrigenda,
repeals, predecessors, successors and directly related delegated or implementing acts form the
bounded history closure. Linked CJEU judgments are a separate source class with judgment dates,
not legislative validity intervals.

When an amended work has no official consolidated expression, Lex holds its official text and
events, reports `consolidation_status=not_published`, and does not claim to know merged current
wording.

For the EU, another domain using the existing legislation classes is a reviewed scope
configuration, preview and backfill. New publishers and document classes with different temporal
semantics still require implementation. Luxembourg is currently complete for Legilux's
`Consolidation` catalogue, not for the broader `Act` catalogue. The measured boundary and required
adapter work are recorded in [Luxembourg scope](luxembourg-scope.md).

## Retrieval policy

Keyword search remains production default until hybrid passes its gate. Hybrid is local: FTS5
BM25 plus a pinned multilingual embedding encoder, compact vectors and fixed RRF. Azure AI Search
and a generative model are not in the retrieval path.

Fuzzy matching is an additive lexical fallback. It runs only when exact lexical search is weak,
protects legal identifiers, dates and quotations, and reports every expansion.

## Activation gate

Hybrid must preserve 100 percent first-result accuracy for exact legal identifiers, produce zero
temporal leakage, improve conceptual nDCG@10 by at least 10 percent relative to keyword search,
regress no more than 2 percent across the complete suite, keep warm server-side p95 at or below
250 ms, and remain below 75 percent of configured memory.

Benchmark queries, relevance judgments, commits, artifacts, machine details and review status are
public. Until external lawyers review a judgment, it is labelled engineer-reviewed.

## Hosting and cost

The current Azure Container Apps Consumption host remains in place. Scale-to-zero fits observed
traffic better than an always-on VM. A VM is reconsidered only after 30 measured days show at
least 20 percent lower complete cost, or Container Apps cannot satisfy the search latency gate.

The deployment uses immutable image tags, a pinned artifact trust root, managed identities and a
candidate revision with smoke tests before traffic promotion. Shared ACR administration is not
disabled until every consumer is audited.

## Risks and containment

- Corpus expansion: preview counts and relationship reasons before acquisition; abort anomalous
  drops and unbounded closure.
- Temporal correctness: fixtures for one version, repeated states, gaps, repeal and absent
  consolidation.
- Storage change: dual-schema reader and byte-identical JSON, timeline and diff goldens.
- Relevance: shadow hybrid mode and public activation gates.
- Supply chain: fail closed on manifest, signature or artifact hash mismatch and retain the prior
  verified release.
- Cost: publish actual resource configuration and measured behavior; do not present retail-price
  estimates as invoices.
