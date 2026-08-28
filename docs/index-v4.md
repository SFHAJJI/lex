# `lex-index/4`

Status: candidate for the V3 rebuild; not active in production until promotion

`lex-index/4` adds a signed, textless structural channel for provision coordinates whose
wording derivation is not safe. Version 3 defines `provisions` as the authoritative occurrence
mapping and gives every occurrence an exact text hash. A second occurrence channel therefore
changes the physical and public truth contract and requires a new schema version.

## Storage contract

- `provisions` remains certified legal text. Every row keeps its exact text hash and participates
  in lexical and semantic retrieval as before.
- `provision_gaps` stores only document order, identity, bounded public metadata, official-source
  binding and one closed reason: `marker_only` or `marker_suspicious`.
- A gap never enters text blobs, lexical states, FTS, semantic chunks, citations or provision
  history hashes.
- The signed content digest and the dedicated provision-gap digest both bind every gap field.
- Every version-4 artifact carries `articles_canon=canon/2`, the gap schema, row count and digest
  stamps, including an audited artifact with zero gaps.

The builder uses a typed compatibility boundary. A null gap capability emits a genuine legacy
`lex-index/3` artifact with no gap table or stamps. Only a `ProvisionGapIndexInput` created from
`canon/2` articles evidence can emit `lex-index/4`, including when its complete row set is empty.
An ordinary list can no longer accidentally select the new schema.

The derived-articles authority is generation-level. `lex-articles-generation/3` remains
byte-compatible and implies `articles_canon=canon/1` without serializing a new field.
`lex-articles-generation/4` requires `articles_canon` for every publisher entry. Opting one
publisher into canon/2 upgrades the generation document and records canon/1 explicitly for every
unchanged publisher. The signed generation digest and articles commit bind that choice. Profile
IDs and per-expression gap fields are consistency evidence, never the schema selector.

## Reader compatibility

The current reader accepts versions 2, 3 and 4. Versions 3 and 4 share the content-addressed text
layout. Only version 4 may carry the provision-gap table or stamps, and version 4 requires the
exact table contract, `articles_canon=canon/2`, and all three gap stamps. A gap table or any of
those stamps on version 2 or 3 is rejected.

This boundary is intentional. A version-3 production binary does not understand structural gaps.
If a gap-aware artifact retained the version-3 stamp, that binary could mount it, report only the
text-bearing rows and describe a partial document as complete. The version-4 stamp makes every old
reader fail closed before serving a request.

## Serving contract

Text rows and gap rows remain separate in storage and API responses. Both carry `document_order`
so a client can reconstruct one outline without treating a gap as legal text. Gap-aware responses
publish text and gap totals plus `text_completeness` as `complete`, `partial` or `unavailable`.
A selected gap is a known coordinate with unavailable text, not an unknown anchor.

Whole-body comparison fails closed when either side has a gap. Structural response truncation is
reported independently and never becomes a claim that certified legal text was truncated.

## Promotion gate

Before promotion, build the new corpus and indexes from frozen commits, verify the signed zero-gap
or nonzero-gap evidence for every artifact, exercise mixed and gap-only documents through web and
MCP consumers, and prove that the production candidate refuses partial whole-body comparisons.
