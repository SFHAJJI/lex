# Extending legal coverage

Lex separates subject expansion from publisher integration. This keeps recurring scope changes
cheap without pretending that a new legal source can be added safely through configuration alone.

## Existing EUR-Lex document classes

Adding another reviewed EU acquisition group is a configuration change in
`src/Lex.Sources.EurLex/eu-scope.json`,
followed by `scope-preview`, review, and a backfill. The preview reports added and removed works,
languages, temporal expressions, relationship-closure causes, missing consolidations, and expected
artifact growth. The shared EUR-Lex adapter, temporal writer, derivation profiles, indexer, search,
MCP tools, and web routes do not change. Group names and selection reasons are engineering inputs;
they never become corpus legal metadata, indexed terms, filters, or evidence.

## A new document class from a known publisher

A class whose dates, relationships, or structure differ from the existing classes receives a
small class strategy inside that publisher adapter. It must define identity, date semantics,
relationship closure, manifestation preference, and extraction confidence. The rest of the
pipeline stays publisher-neutral. Examples include CJEU case law and Luxembourg as-published
normative acts, where a publication date is not interchangeable with a consolidation validity
date.

## A new publisher or jurisdiction

Implement `ISourceAdapter`, register it once in `SourceAdapterRegistry`, and add the relevant
deterministic extraction profile. The adapter owns official-source access, publisher identifiers,
document types, languages, dates, relationships, attribution, and licensing evidence. Ingestion,
verification, lifecycle events, derivation, index v3, filters, MCP tools, and mounted-index routing
consume shared contracts.

Search jurisdiction filters are matched against each mounted index's `jurisdiction` stamp. They
are not mapped through a Luxembourg/EU switch, so another jurisdiction becomes searchable when
its verified index is mounted.

The search workspace derives its jurisdiction, hierarchy, act-form, binding-status, and language
controls from mounted indexes. The legacy generic domain field is empty in current v4 builds.
Official classifications are returned as typed publisher metadata and may be selected again by
their exact official URI; acquisition labels never enter that path.

## Required evidence before publication

Every expansion must pass:

1. A reviewed scope preview or publisher catalogue comparison.
2. Corpus integrity verification over records and publisher bytes.
3. Deterministic derivation and explicit extraction-profile confidence.
4. Index build, signed whole-artifact manifest, and retrieval benchmarks.
5. A zero-traffic Azure candidate revision with Luxembourg and EU smoke tests.

No adapter may synthesize consolidated wording. When an official consolidation does not exist,
Lex stores the official expression and amendment timeline and reports that merged current wording
is unavailable.
