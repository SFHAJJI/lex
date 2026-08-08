# Local hybrid retrieval

Status: implemented behind the activation gate

Lex combines filtered FTS5/BM25 with a local `intfloat/multilingual-e5-small` encoder. Azure AI
Search and generative models are not in retrieval. The model is pinned to revision
`614241f622f53c4eeff9890bdc4f31cfecc418b3`; the runtime verifies the model and tokenizer hashes
before opening ONNX Runtime. The model card specifies 384 dimensions and the required `query:`
and `passage:` prefixes: <https://huggingface.co/intfloat/multilingual-e5-small>.

The selected qint8 ONNX artifact is about 118 MB, plus a 5 MB SentencePiece model. This is a
candidate cost optimization. The current Container App is sized at 2 GiB from the measured
working set and remains below the activation gate's 75% memory ceiling.
The public benchmark records the actual working set, and hybrid cannot activate above 75 percent
of the configured memory limit.

## Offline path

1. A provision at or below 256 model tokens becomes one semantic chunk.
2. Longer provisions split at paragraph boundaries. An individually oversized paragraph splits
   at a tokenizer boundary. Adjacent chunks retain up to 32 tokens of overlap.
3. Each work, language, anchor and distinct wording is embedded once with the `passage:` prefix.
4. Unique chunks are embedded in deterministic, bounded batches. Batch size is an indexing
   resource setting; it does not change chunk order, vector ordinals or the final monolithic
   SQLite/vector artifact pair.
5. The vector file stores a 1-bit sign vector for the first scan and an int8 normalized vector for
   reranking. The file is memory-mapped, so repeated copies are not loaded into managed memory.
6. SQLite maps each chunk and vector ordinal back to the authoritative lexical state. The vector
   file, model, tokenizer, scope and SQLite index must all appear in the signed artifact manifest.

Index builds report four stages separately: deterministic text/chunk preparation, ONNX embedding,
SQLite/FTS population, and digest/commit/vector finalization. Each stage exposes its own completed
count, percentage, elapsed time and ETA so a quiet CPU, transaction or publication pass cannot be
mistaken for a stalled model run.

## Query path

Exact CELEX and ECLI identifiers are prioritized. Keyword candidates and semantic candidates are
filtered by date, language, hierarchy, act form, binding status and domain before ranking. The
semantic path scans sign bits, int8-reranks at most 500 candidates and returns the best 100 states.
Keyword and semantic ranks fuse with reciprocal rank fusion at fixed `k=60`. Repeated wording is
collapsed to the exact eligible occurrence.

Fuzzy matching is a lexical fallback only when exact keyword search returns fewer than five
qualified results. Edit distance is limited to one for ordinary words and two for words of eight
or more characters. Dates, digits, short terms, quoted phrases, CELEX/ECLI identifiers and article
markers are protected. The response publishes every expansion.

## Public evaluation

`RetrievalBenchmarkCatalog` publishes exactly 200 engineer-reviewed retrieval judgments across EU
and Luxembourg: exact professional names and identifiers, temporal, conceptual, bilingual, fuzzy,
hierarchy/filter, role, multi-work comparison, negative, ambiguity and known-gap cases. Every
relevant identity includes both collection and work, and every case is frozen into a tuning or
holdout split. These are retrieval labels, not legal conclusions.

`/benchmarks/cases.json` exposes them. The benchmark command selects the cases for the mounted
collection and records commits, manifest, model, machine/resource configuration, sample count,
index and vector size, process memory, model load, cold query and warm p50/p95/p99. Reports expose
tuning and holdout metrics separately, including no-hit, resolution and role-intent accuracy.
Missing configuration fails the gate rather than becoming an estimate. Only holdout measurements
can authorize a default change.

`evals/retrieval-baseline-v2.json` binds this pre-tuning case set and split by SHA-256. Its runtime
measurement status remains explicitly pending until the signed production candidate indexes exist;
the release benchmark replaces that absence with measured artifact, machine and commit identities.

`evals/weak-enrichment-decision.json` records the separate weak-discovery activation decision. The
reviewed EU enrichment artifact currently contains no model-derived discovery records, so weak FTS
and concept-vector fields are not eligible for ablation and remain excluded from every public search
default. Any future candidate artifact is capped per work and kind, and every evidence anchor and
text hash is revalidated against the held index during each immutable rebuild before benchmarking.
