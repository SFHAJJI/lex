# Enrichment sample v1

Status: experiment evidence only. Nothing in this report was indexed, signed, published, or
deployed.

## Reproducible inputs

- Captured: `2026-08-08T14:46:18.5133149Z`
- Azure endpoint host: `oai-soufien-dev.openai.azure.com`
- Deployment: `gpt-5-mini`
- Prompt SHA-256: `1baa6303e8fa3e4da5d1d83a15aaaa45784c67814bae9019a7fb8435a4fe020d`
- Runs per held work: `2`
- Raw report: `C:\lex-retrieval-agent-evidence-v2\reports\enrichment-sample-v1.json`
- Raw report bytes: `184039`
- Raw report SHA-256: `a03b11dc19ceba01dec84a79592d34c65675bcafa2b3630cd4a418b2ba3fae86`
- Input tokens: `101488`
- Output tokens: `28462`

The raw report holds model responses, token counts, exact work/version/anchor/text-hash evidence,
accepted proposals, and every rejection reason. It is kept outside Git because it is experiment
evidence rather than a product fixture.

## Strict-consensus result

| Work | Valid model items | Strict proposals | Accepted |
| --- | ---: | ---: | ---: |
| GDPR (`32016R0679`) | 24 | 24 | 0 |
| DORA (`32022R2554`) | 28 | 27 | 0 |
| AI Act (`32024R1689`) | 28 | 28 | 0 |
| Luxembourg CNPD law | 24 | 22 | 1 |
| Net-Zero Industry Act (`32024R1735`) | 0 | 0 | source evidence unavailable |

The only strict weak-field acceptance was `recours Tribunal administratif contre décisions CNPD`.
No alias or acronym was automatically promoted. `32024R1735` is not in the mounted corpus used by
the experiment and remains a coverage finding; enrichment must never manufacture around a missing
source work.

Exact normalized-string consensus was safe but too literal. Independent runs found materially the
same GDPR, DORA, and AI Act concepts with different wording and sometimes different subsets of the
same exact provision evidence.

## Pinned semantic-consensus analysis

The follow-up analysis used the already pinned local `intfloat/multilingual-e5-small` model. It
matched only weak proposals from different runs that shared at least one exact held provision hash;
strong aliases and acronyms were excluded. The analysis artifact is:

- `C:\lex-retrieval-agent-evidence-v2\reports\enrichment-semantic-analysis-v1.json`
- SHA-256: `b70705e38cc4295dff5f87b4c2cecb32c45bda59ecb76ce0daef1d8b07b75fd2`

At cosine `>= 0.90`, the candidate accepted counts were GDPR `9`, DORA `6`, AI Act `8`, and the
Luxembourg CNPD law `3`. Inspection found no incoherent pair in this small sample. `0.925` dropped
the useful GDPR breach-notification pair, while `0.80` accepted more plausible pairs but leaves less
safety margin. `0.90` therefore advances to retrieval testing; it is not a production threshold yet.

## Taxonomy decision under test

Publisher-held controlled concepts must outrank model classifications. EuroVoc provides the
multilingual controlled-vocabulary backbone. Model output may propose mappings to versioned stable
taxonomy identifiers and may produce evidence-anchored free-form provision concepts:

- reviewed/publisher taxonomy identifiers may become lawyer-facing filters;
- free-form provision concepts remain weak retrieval-only signals;
- new taxonomy terms require review;
- tags attach to a distinct work or provision text state, never every repeated temporal occurrence;
- protected legal text, titles, identifiers, dates, hierarchy, status, relationships, and hashes are
  not writable by enrichment.

The `0.90` weak proposal set advanced to the work-level retrieval experiment documented in
`work-retrieval-summary.md`. No enrichment has graduated to a signed or deployed product artifact.
