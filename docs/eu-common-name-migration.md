# EU official-name migration

Legal names and classifications come only from official publisher data. EUR-Lex titles and
`title_short` values remain separate source fields. A literal comma-delimited segment of an
official publisher short title may identify a work when that normalized segment is unique in the
effective catalogue. A collision is an explicit clarification, never an arbitrary choice.

Assigned EuroVoc concepts, official alternative labels, immediate broader relations,
micro-thesaurus/subdomain coordinates, directory coordinates, and publisher short titles are
stored as typed publisher metadata. Only the short-title rule above can establish work identity;
all other metadata is weak discovery context and is not legal-text evidence.

There is no manually maintained legal-name or alias file. The former
`config/eu-work-enrichment.json` input and its digest were removed.

## Required v4 build sequence

1. Fresh-ingest the EU corpus with the exact engineering scope file. Corpus provenance records
   `source_configuration_kind=engineering_scope` and the SHA-256 of the raw, LF-pinned scope
   bytes.
2. Verify the v4 corpus, then derive it once. The single top-level
   `lex-articles-generation/3` manifest binds corpus and deriver identities.
3. Build and verify the index without any enrichment argument. Official metadata is already in
   the corpus bytes and therefore covered by corpus, generation, and signed-index provenance. The
   source-backed citation-identity bit advances the internal work catalogue to version 3, so a
   version-2 database must be rebuilt rather than mounted by the new reader.
4. Smoke unique short titles and at least one deliberate collision. Verify taxonomy matches are
   reported as publisher-metadata discovery rather than text or identity authority.

The old enrichment-file workflow is incompatible with this migration. Rollback must restore the
prior immutable corpus and signed index together; never combine artifacts across the boundary.
