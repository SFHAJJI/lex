# Baseline: full copied LU/EU indexes

Captured 2026-08-08 from the inputs fixed in `input-manifest.json`. The raw report is
`C:\lex-retrieval-agent-evidence-v2\reports\baseline-v2.json`; it is evidence, not a release
artifact. Judgments are engineer-authored and are not claimed as lawyer-reviewed.

## Retrieval observations

| Query | Keyword | Hybrid | Observation |
|---|---:|---:|---|
| `32016R0679` | GDPR #1 | exact lookup uses keyword, GDPR #1 | canonical identifier works |
| `RGPD` | no result | wrong work #1 | missing French professional-name representation |
| `GDPR` | GDPR #1 | GDPR #1 | current adapter short title happens to cover English |
| full French RGPD name | GDPR #2, corrigendum #1 | GDPR absent | base-work/name resolution is missing |
| `DORA` | DORA #1 | DORA #1 | current adapter short title happens to cover it |
| `AI Act` | AI Act #1 | AI Act #1 | current adapter short title happens to cover it |
| long RGPD + 72-hour question | GDPR absent | GDPR absent | names and provision evidence are not combined |
| descriptive 72-hour breach question | GDPR absent | GDPR absent | descriptive recall/query formulation fails |
| ICT third-party financial risk | DORA #1 | DORA #1 | provision evidence is already sufficient |
| photovoltaic resilience/auction question | target absent | target absent | requires corpus check plus discovery enrichment |
| explicit GDPR corrigendum | target #2 | target #4 | document role/identifier ranking is weak |
| Luxembourg CNPD organisation | target #1 | target #1 | existing exact title/body retrieval works |

Keyword cases took 67–396 ms on this machine. Full local CPU hybrid cases took 2.9–17.3 seconds.
Those values are diagnostic, not production latency claims.

## Authoritative identity baseline

The report records row counts and SHA-256 digests for `docs`, `text_blobs`, `provisions`,
`provision_states`, `anchor_events`, `citations`, `events`, and `obs_history` in both indexes.
It also records exact response digests for:

- GDPR Article 33 as held on 2019-03-15;
- the GDPR comparison from 2019-03-15 to 2025-01-01;
- the current Luxembourg CNPD-law outline.

Every enriched candidate must reproduce all protected-table and representative-evidence digests.

## Workbench extraction

Two independent builds materialized 3,899 latest work-language publisher records from the copied
indexes. Both 3,600,384-byte SQLite workbenches have SHA-256
`8cb8cde959cdbbef6e3244cb4cc03fdbc3fcb17047e7cc0d11ab34161f868902`. The extraction is therefore
byte-deterministic on the measured machine. One real record without a publisher title is preserved
as null rather than assigned generated official metadata.
