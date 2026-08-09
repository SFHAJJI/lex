# EU professional-name migration

EUR-Lex professional names are no longer written into corpus titles by adapter code. Official
titles and publisher short titles remain separate. Exact professional names come from
`config/eu-work-enrichment.json` as reviewed aliases; its model-discovery section is empty.

## Required build sequence

1. Re-ingest the EU corpus with the current adapter. This refreshes work titles and every
   expression `title_short`, and stamps `publisher_discovery_schema=publisher-discovery/1` in the
   corpus manifest.
2. Re-derive articles from that corpus.
3. Build the EU index with
   `--work-enrichment config/eu-work-enrichment.json`.
4. Verify the index stamp contains `enrichment_digest`, then smoke `GDPR`, `RGPD`, `DORA`, and
   `AI Act` as `exact_alias` matches.

The index command refuses to combine reviewed aliases with a corpus that lacks the migration
marker. Reduced scopes and single-language builds deterministically retain only aliases for
work-language records actually held by the corpus.

## Rollback

Keep the prior immutable corpus and signed index release. Roll back both code and artifacts
together; do not mix a pre-migration corpus with the reviewed alias file.
