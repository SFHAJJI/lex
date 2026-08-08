# Work-level retrieval experiment v8

Status: experiment evidence only. Neither the catalog nor its vectors were signed, published, or
deployed.

## Design tested

The candidate index keeps authoritative legal text untouched and adds two compact retrieval
artifacts:

- one SQLite work catalog with separately weighted official identifiers, reviewed professional
  aliases, official titles, publisher facets, and weak evidence-anchored discovery concepts;
- one vector for each work's names/facets plus one separate vector for each accepted discovery
  concept, using the already pinned multilingual E5 model and vector format.

Keyword search can match a concept literally. Hybrid search can match a semantic neighbour of a
concept without appending tags to provision text or repeating metadata for every provision. Fixed
RRF (`k=60`) fuses lexical and semantic candidates; work-name/title semantics use weight `0.65`
and weaker generated concept evidence uses `0.60`. Exact identifiers, aliases, and titles remain
pinned ahead of semantic evidence.

## Reproducible result

- Evaluation: `C:\lex-retrieval-agent-evidence-v2\reports\work-hybrid-eval-v8.json`
- Evaluation SHA-256: `bdb037fc2eb8ba5e7075c974c5b80767cf6a915cd552011386cf38c27a41dcb5`
- Catalog bytes: `16384000`
- Catalog SHA-256: `e5f0fd1cfb3803f779b9ef98f58f9c2c0d4943ea18a60cdc49e36b21ed9cbab0`
- Vector bytes: `1695632`
- Vector SHA-256: `bcfebfba2a582b3d14d96254910e0e20445c4431207c4dc8ed86560f9654e1f1`
- Vector-input SHA-256: `689e3514040008340609b3bf311ae66250144b3297b60b3500b13c3575fdeb88`
- Final verification: `C:\lex-retrieval-agent-evidence-v2\reports\work-vector-verify-v4-final.json`
- Verification SHA-256: `0850fdc4f7ae43bf7beba9837bdbd95bf5d3723b66b186c9868f3dc9961160c7`
- Work/language base vectors: `3899`
- Weak concept vectors: `26`
- Total vectors: `3925`
- Warm p50 over this small 12-case suite: `65.13 ms`
- p95 including first cold model query: `168.60 ms`

All frozen ranking gates passed. Every EU positive case ranked the intended work first. The
Luxembourg CNPD law ranked second, inside its specified top-three gate. The photovoltaic-resilience
target remained absent because the required Net-Zero Industry Act is not in the frozen corpus.
Retrieval correctly did not invent it.

## Failed variants and correction

One aggregate vector per work diluted the useful concepts and made descriptive ranking worse. One
separate concept vector per accepted concept fixed that failure. The first hybrid pass then ranked
the base GDPR act above its corrigendum for an explicit `rectificatif` query. Raising or lowering
scores would have been a workaround: the missing field was publisher document role. Adding the
generic role field and applying it as query intent restored the corrigendum to rank one. A later
review found concept and base-work vectors had equal fusion weight. `0.35` made useful descriptive
discovery regress; the measured `0.60` weight is lower than the `0.65` base-work weight and passes
all frozen gates. This is a sample calibration, not a universal production constant.

## Verdict

This validates the user's proposed retrieval shape on the frozen sample: literal tags contribute to
keyword retrieval, concept vectors contribute weaker synonym evidence to hybrid retrieval, and
exact legal names/identifiers retain deterministic priority. It does not validate the model-created
taxonomy globally. Production still requires publisher EuroVoc retention, reviewed aliases,
larger negative/relevance sets, signed-manifest coverage, and the existing activation gates.
