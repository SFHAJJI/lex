# Data authority

The evidence layer is append-only publisher material. The consumption layer is reproducible and
disposable. That split lets an extraction improve without rewriting what the publisher served.

![Legilux and EUR-Lex feed evidence repositories, deterministic profiles, article records and signed indexes, with hashes and source URIs carried throughout.](/built/diagrams/data-authority.svg)

[Open the data authority diagram at full size](/built/diagrams/data-authority.svg)

## Ingestion and derivation

| Stage | Input | Output | Owner | Refusal boundary |
|---|---|---|---|---|
| Enumerate | Official publisher catalogue | Works, states, expressions and manifestations | `Lex.Sources.Legilux` or `Lex.Sources.EurLex` | A partial or implausible inventory commits nothing |
| Acquire | Official URLs | Verbatim bytes and observation records | publisher adapter plus evidence repository | Existing evidence is not silently overwritten |
| Derive | Stored bytes plus immutable extraction profile | Article records and typed gaps | `Lex.Derive` | Unsafe structure remains unavailable |
| Index | Hash-pinned corpora and article release | SQLite FTS, temporal tables and optional vectors | `Lex.Ingest` and `Lex.Index` on the local build machine | Incomplete or mismatched inputs fail the build |
| Sign | Canonical artifact manifest | Signature and public verification material | bounded `lex-ops` publication with Key Vault | Runtime rejects a manifest outside pinned trust |

Extraction profiles are versioned and their fingerprints are frozen by test; an improvement ships
as a new profile version, and the Memorial fallback ladder promotes the successor only when it
recovers strictly more wording. Evidence is fetched once per address: a later silent edit behind
the same address is structurally invisible, which is exactly why every observation binds what Lex
saw and when it saw it.

## Official discovery metadata

Luxembourg acquisition mirrors Legilux `subjectLevel1` and `subjectLevel2` assertions with the
concept URI, French label, level, language and source. EU acquisition mirrors official short-title
segments, EuroVoc assigned, alternative and broader relations, and directory classifications with
their identifiers and source URIs. The two vocabulary systems remain distinct.

These values enter a weak, explainable discovery lane. They may surface a work whose provisions do
not contain the query phrase and the result can say which official classification matched. They do
not establish legal identity, support a legal claim or become historical evidence. No manual legal
alias catalogue and no model-generated metadata enter production authority.

## What the cryptography proves

| Mechanism | Proves | Does not prove |
|---|---|---|
| SHA-256 of a file or provision | The bytes read now equal the bytes previously addressed by that digest | Who published the bytes or whether the law is valid |
| Publisher URL and observation record | Where and when Lex acquired the material | That a later copy was not modified |
| Signed whole-artifact manifest | A controlled release identity approved this exact set of indexes, vectors, encoder and scope files | Legal correctness by itself |
| Signed evaluation and promotion receipts | The exact candidate passed the stated gates and became the named revision | Future behavior under every possible question |

Origin, integrity and release authorization are separate claims. Keeping all three is why a
citation can be checked without treating a hash as a magical certificate of truth.
