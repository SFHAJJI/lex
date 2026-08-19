# `lex-index/3`

Status: shipped and verified in production

`lex-index/3` changes physical storage without changing legal identity or the public comparison
contract. Each dated version still has one occurrence row for every provision. The occurrence
points to exact UTF-8 text addressed by SHA-256, so repeated wording is stored once while every
date, work, anchor and validity interval remains queryable.

## Storage model

- `provisions` is the authoritative occurrence mapping. It retains version identity, order,
  anchor, provision identity, metadata and the exact text hash.
- `text_blobs` stores one payload per text hash. Brotli quality 4 is used only when it saves at
  least 8 percent; otherwise UTF-8 remains raw. The reader verifies decompressed size and SHA-256.
- `lexical_states` stores one searchable state per work, language, anchor and distinct wording.
- Contentless FTS5 stores terms only. Search joins a state back to an eligible dated occurrence,
  preventing repeated versions from producing duplicate hits.
- Hierarchy, act form, binding status and consolidation status are columns on document
  occurrences. The legacy generic `domain` column remains empty in current builds. Official
  classifications are typed publisher metadata, returned as `matched_publisher_metadata` and
  selected again only by their exact official URI. These are filters, not database boundaries.

## Compatibility and safety

The reader explicitly accepts `lex-index/2` and `lex-index/3`; every new build emits version 3.
Unknown schemas still fail closed. Version 3 reconstructs provision text before rendering,
structured JSON, timeline display, comparison or diff. Semantic chunks are not part of this
schema slice and will never be passed to the comparison engine as legal text.

The signed content digest still commits to every document identity and occurrence text hash. In
version 3, digest verification also decompresses and verifies every unique blob. The release
artifact manifest separately covers the complete SQLite file.

## Rollout check

Before a version-3 index replaces version 2, build both from the same corpus commit and compare:

1. work, version and provision occurrence counts;
2. ordered provision hashes for every version;
3. reconstructed bodies and comparison results;
4. keyword results for the public benchmark;
5. database size, build time, memory and warm and cold latency.

The production architecture page reports the schema read from mounted indexes. Verified version-3
EU and Luxembourg artifacts were promoted together on 2026-08-09.
