# Lex

[![CI](https://img.shields.io/github/actions/workflow/status/SFHAJJI/lex/ci.yml?branch=main&label=tests&style=flat-square)](https://github.com/SFHAJJI/lex/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue?style=flat-square)](LICENSE)
[![Live](https://img.shields.io/badge/live-law.soufien.lu-e0705f?style=flat-square)](https://law.soufien.lu)
[![MCP](https://img.shields.io/badge/MCP-read--only%20tools-6f42c1?style=flat-square)](https://law.soufien.lu/developers)
[![Coverage](https://img.shields.io/badge/corpus-live%20coverage-brightgreen?style=flat-square)](https://law.soufien.lu/coverage)

**Point-in-time retrieval of regulatory text.** Regulators publish the current
rule; every audit, investigation and dispute is about a **past date**. Lex keeps
every version it has seen and answers *"what did this say on 15 March 2022?"*
with the exact validity interval, the timeline, the instrument that changed it,
and a hashed provenance record, and an honest, machine-readable refusal when it
cannot know.

**[Live demo](https://law.soufien.lu)** ·
**[Ask the AI](https://law.soufien.lu)** ·
**[MCP endpoint](https://law.soufien.lu/mcp)** ·
**[Dataset (CC-BY)](https://github.com/SFHAJJI/lex-articles)** ·
**[Examples](https://github.com/SFHAJJI/lex-articles/tree/main/examples)** ·
**[Architecture](https://law.soufien.lu/architecture)** ·
**[Next architecture](https://law.soufien.lu/architecture/next)** ·
**[Benchmarks](https://law.soufien.lu/benchmarks)** ·
**[Verify it yourself](https://law.soufien.lu/verify)** ·
**[Program](docs/hybrid-eu-roadmap.md)** ·
**[Spec (D1-D60)](docs/lex-spec-v4.md)**

## Try it in 30 seconds

Give any MCP-capable AI the full toolset, no key, no install:

```
claude mcp add --transport http lex https://law.soufien.lu/mcp
```

Or ask the [live site](https://law.soufien.lu). A real answer, verbatim:

> **Q: What did CRR Article 92 require as capital ratios on 1 March 2020, and has that text changed since?**
>
> Quoted verbatim (Article 92(1)) from the CRR version in force on that date:
> *"Subject to Articles 93 and 94, institutions shall at all times satisfy the
> following own funds requirements: (a) a Common Equity Tier 1 capital ratio of
> 4,5 %; (b) a Tier 1 capital ratio of 6 %; (c) a total capital ratio of 8 %."*
>, `eu-eurlex:32013r0575:2019-12-25` (valid 2019-12-25 → 2020-06-26),
> [permalink](https://law.soufien.lu/eu-eurlex/32013r0575/2019-12-25#art_92).
>
> Article 92 has had **four distinct texts** since 2013-2013-06-28 → 2021-06-28,
> 2021-06-29 → 2022-12-31, 2023-01-01 → 2024-12-31, 2025-01-01 onward, each with
> its own permalink and sha256.

Every claim in that answer came from a deterministic tool call (the trace is
shown under each reply); the model never answers from its own memory.
Do not take this file's word for it, the numbers above are checkable in one call,
and if they ever drift from the live system, that is a bug worth reporting:

```bash
curl -s -X POST https://law.soufien.lu/mcp -H 'Content-Type: application/json' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"article_history",
       "arguments":{"work":"eu-eurlex:32013r0575","anchor":"art_92"}}}'
```

## Who uses this

- **A compliance officer** checking which text of an obligation was in force on
  the date of the facts, with a permalink and a hash for the file.
- **A legal-tech developer** building RAG over law that must not hallucinate
  versions: per-article chunks with `valid_from`/`valid_to` to filter *before*
  similarity ([dataset](https://github.com/SFHAJJI/lex-articles)).
- **An AI agent** using the MCP tools directly, the same tools the site's
  own AI uses, at the same endpoint.
- **A researcher** tracking how one article's text evolved across amendments
  (`article_history`: every distinct text state, dated).

## What it never does

Lex answers *what the rule was*. It does not decide "were we compliant?", "does
this apply to me?", or "what does this mean?", those are professional opinions.
The evidence, index and MCP layers never generate or interpret legal text
(fitness rule F10). The optional assistant may explain retrieved evidence, but
it is visibly separate, carries the tool trace and is not part of the record.

## Architecture (one screen)

```
APPS        Lex.Ingest (CLI)   Lex.Mcp (MCP server)        Lex.Web (demo)   Lex.Ask (AI loop)
DERIVED     Lex.Derive, evidence -> per-article Markdown+JSON (immutable profiles: akn-lu/1, fmx4-eu/1, xhtml-eu/1)
ADAPTERS    Lex.Sources.Legilux (Tier A, SPARQL)   Lex.Sources.EurLex (Tier A, Cellar + Formex)
MODEL       Lex.Law, Publisher, Work, Version, Expression, Observation. No publisher names.
FOUNDATION  Lex.Temporal (interval algebra)   Lex.Index (SQLite: filter-first, verified artifacts)
```

- **One corpus repo per publisher**; the corpus is human-readable JSON + git. The tree carries the legislative history, `git log` carries the ingest history, and the two are deliberately not the same ([why](https://law.soufien.lu/decisions)).
- **Bitemporal**: valid time is the publisher's; transaction time is ours, as
  append-only observation chains inside hashed content. Nothing is ever
  overwritten; publisher corrections become visible events.
- **Two layers**: verbatim publisher bytes (evidence) → deterministic
  per-article extraction (consumption). Every derived article hash-chains to
  the exact bytes the state published; `lex verify derive` re-derives and
  byte-compares.
- **Filters before ranking, always**, enforced by construction (a non-optional
  `FilterSet` on the only query entry point).
- **Signed whole-artifact manifests** (ECDSA P-256): a trust root pinned in the
  application verifies indexes, vectors, embedding assets, scope, benchmark
  and source commits before any file is mounted. The embedded index stamp
  remains public provenance, not its own trust root.
- **Honest refusals**: `no_version_for_date`, `anchor_not_in_version`,
  `outside_observed_window`, `text_withheld`, a flagged wrong answer is still
  a wrong answer, so Lex refuses instead.

## Current coverage

**Luxembourg** (Legilux, Tier A): every work and version in the publisher's
consolidated collection. Nothing in that collection is filtered out by type. The mounted counts, date
range, corpus commit and extraction-profile mix are read directly from the
index on the [live coverage page](https://law.soufien.lu/coverage), rather than
copied into prose that becomes stale after the next publisher run.
Text is verbatim Akoma Ntoso XML from the publisher's official,
robots-permitted filestore, licensed CC-BY-4.0.

**Text is held for 2,949 of those versions, not all of them, and the reason is
the publisher's format rather than our pipeline.** Legilux offers XML for 2,892
consolidations, PDF only for 1,611, and no file at all for 130 (measured against
its own catalogue, 2026-08-04). Lex reads the XML, because XML is the only format
carrying article boundaries, which is what makes an article citable, hashable and
diffable.

Where the publisher issues no XML, Lex falls back to the consolidated PDF
(profile `pdf-lu/1`, spec D49). Those PDFs are born-digital with a real font
layer, so no OCR is involved: 64 versions are read this way, and the profile id
records per version that the article boundaries were inferred from typography
rather than taken from publisher markup. The fallback deliberately refuses the
1,371 thematic-collection PDFs, which concatenate every act on a shelf, and the
176 Memorial gazette scans, where the act sits inside a whole day's journal.
Everything else keeps its dated record, source and hash, with no wording.

The gap is concentrated outside the hierarchy of norms, not across it:

| | text held |
|---|---|
| Constitution, treaties | **100%** |
| Code (enacted as a law) | **100%** |
| Règlement de la Chambre, arrêté ministériel | **100%** |
| Règlement grand-ducal | 96% |
| Loi | 93% |
| Règlement ministériel, arrêté grand-ducal | ~75% |
| RECUEIL / CODE_RECUEIL (thematic folders, not instruments) | 9% / 2% |

Roughly 1,371 of the textless versions are those folders, which nobody voted and
which hold no rule of their own.
Honest coverage claim: *dense and reliable from 2017 onward; real but sparse
before; isolated snapshots back to 1849; forward to 2030.*

**EU** (EUR-Lex/Cellar, Tier A): a reviewed compliance shelf spanning data,
digital, cyber, finance and energy law. The mounted index and
[live coverage page](https://law.soufien.lu/coverage) are the source of truth for
work and version counts. Full text comes from the Publications Office's
**Formex 4** structural XML where served, including large consolidations the
XHTML channel cannot carry. The present EU limit is scope, not format.

The derived dataset publishes its current counts and source commits in its
[release catalog](https://github.com/SFHAJJI/lex-articles/blob/main/catalog.json).
The never-consolidated LU acts and broader approved EU scope are handled by the
[temporal expansion program](docs/hybrid-eu-roadmap.md). Communal regulations are deliberately out of scope: 17,232
exist as published acts, none is ever consolidated, so there is no point-in-time
history to hold. The fallback ladder for XML-less versions is spec D49.

## Run it

```
# ingest (paced, sequential; official open-data channels only)
dotnet run --project src/Lex.Ingest -- ingest --publisher lu-legilux --corpus ../lex-corpus-lu-legilux

# derive the per-article layer, build the signed index
dotnet run --project src/Lex.Ingest -- derive --publisher lu-legilux --corpus ../lex-corpus-lu-legilux --out ../lex-articles
dotnet run --project src/Lex.Ingest -- index --corpus ../lex-corpus-lu-legilux --articles ../lex-articles \
    --out indexes/index-lu-legilux.db --keyfile signing-key.pem

# web demo + MCP (stdio) locally
LEX_INDEX_DIR=indexes dotnet run --project src/Lex.Web
LEX_INDEX_DIR=indexes dotnet run --project src/Lex.Mcp
```

The generated key above is for local development only. Production publication
uses GitHub OIDC to ask the non-exportable Azure Key Vault key to sign the
whole-artifact manifest, then deploys a zero-traffic candidate revision.

## MCP tools

`as_of` (full / outline / per-article select) · `timeline` · `in_force_on` ·
`diff` · `search` · `article_history` · `provenance` · `coverage` ·
`cited_by` · `changes_in_period`.

The same read-only tools cover Luxembourg and EU material. Search spans every
mounted publisher by default and can filter jurisdiction, date, hierarchy,
legal form, binding status, domain and language. Keyword retrieval is
deterministic FTS5/BM25. Hybrid adds the pinned local encoder and fixed rank
fusion when verified vectors are mounted; no generative model participates in
retrieval. `coverage` exists to say what Lex does **not** have, because a system
that cannot state its own gaps cannot be trusted with a completeness question.

## Contributing

Issues and PRs welcome, the highest-leverage areas:

- **A new publisher adapter** (`ISourceAdapter`, ~200 lines): any jurisdiction
  with an official machine-readable channel. The seam is publisher-pure by
  fitness test; adapters never touch files or git.
- **Eval cases** ([evals/cases.json](evals/cases.json)): questions where the AI
  should construct better tool calls, or refuse better.
- **Extraction improvements**: profiles are immutable; improvements ship as a
  *new* profile beside the old (see `fmx4-eu/1` beside `xhtml-eu/1`).

Contributions are accepted under the Developer Certificate of Origin
(`git commit -s`).

## Licence

Code: **Apache-2.0** ([LICENSE](LICENSE)). The code licence does **not** extend
to corpus data or index artefacts, see each corpus repository's `NOTICE`
(three layers: official acts outside copyright / Lex's compilation rights /
code licence inapplicable). Derived dataset: CC-BY-4.0 (LU) and EU
reuse-with-attribution, licence inline in every file.

## Support

This is free and open, and it stays that way whatever you decide. It is also not free to run:
the live site, the nightly jobs and the storage sit on Azure infrastructure I pay for out of
pocket, and I maintain it on my own time.

If it saved you an afternoon, you can [buy me a coffee ☕](https://buymeacoffee.com/shajji)
and put it towards the hosting bill. Starring the repo helps just as much, and costs nothing.
