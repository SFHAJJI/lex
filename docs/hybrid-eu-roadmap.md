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

## Evidence export contract

The article reader and temporal comparison can export a transparent Markdown reading aid and
copy a citation. The export is built lazily from the same structured MCP payload and diff pieces
already displayed on screen. It does not fetch a second version, run another comparison, alter
legal wording or send text to a model. It records the work and version identifiers, validity
dates, language, Lex permalink, official publisher sources, extraction profile and full provision
text hashes. A one-article comparison records both the before and after hashes.

Generated PDF is deliberately not the first export format. A polished PDF can be mistaken for an
official publication, adds a rendering dependency and makes provenance harder to inspect.
Markdown is portable, diffable and visibly a reading aid. Every file states that it is neither an
official publication nor legal advice; the publisher source remains the authority.

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
public. Generated candidates are labelled `generated-unreviewed` and cannot authorize activation.
Engineer-reviewed and lawyer-reviewed judgments are identified individually.

## Hosting and cost

The current Azure Container Apps Consumption host remains in place while the verified mounted
index set is small enough to ship and cold-start safely. Durable, immutable release artifacts live
in Azure Blob Storage, but SQLite, FTS and vector files are never queried over Blob or Azure Files:
the serving process always reads a verified local copy.

Hosting changes are driven by measured release evidence rather than an optimistic size estimate:

- a single artifact above 2 GiB is published through Blob instead of GitHub Releases;
- a mounted index set above 2 GiB triggers a zero-traffic VM candidate benchmark;
- a mounted index set above 4 GiB, a failed cold-start/latency/memory gate, or insufficient ACR
  allowance to retain both production and rollback images requires a VM with a managed data disk;
- the disk is the next managed-disk tier that can hold the active, previous and incoming verified
  release sets plus 10 percent working headroom.

The VM runs the same container. It downloads and verifies a release from Blob into a versioned
directory, warms it, then switches an atomic `current` link. The old Container App revision and
the preceding disk release remain rollback paths until the new host passes live acceptance. This
avoids remote random reads and prevents a large legal corpus from becoming a container-image layer.

The deployment uses immutable image tags, a pinned artifact trust root, managed identities and a
candidate revision with smoke tests before traffic promotion. Shared ACR administration is not
disabled until every consumer is audited.

## Offline index builds

The signed index is portable; its build machine is not part of the serving architecture. Routine
small updates may run on a CPU Fleet runner, while a large first semantic backfill may run on a
reviewed local GPU or temporary build worker. Both paths execute the same chunking, ordering,
quantization, database and manifest code. The execution provider and ONNX Runtime version are
recorded in the index stamp, and retrieval benchmarks run against the finished artifact before it
can be promoted.

Embedding work is cached by chunk SHA-256 and by a profile covering the model, revision, model and
tokenizer hashes, vector format, dimensions, runtime and execution provider. The cache commits each
completed batch. An interruption therefore resumes from verified content-addressed results, and a
later scope expansion embeds only new or changed chunks. The cache is build evidence, never a query
database or a released source of legal text; the final vector file remains one deterministic,
ordinal-checked artifact rather than glued partial indexes.

GPU inference uses a small fixed set of token shapes only after the authoritative chunk boundaries
have been selected. Chunks are grouped into 32, 64, 128, 256 and 512-token buckets; padding is masked
and exists only inside the inference tensor. It cannot change stored wording, hashes, citations,
vector ordinals, temporal occurrences, rendering or comparison. The embedding profile is part of
the cache key and index stamp so a shape-policy change cannot silently reuse incompatible evidence.

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
