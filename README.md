# Lex

**Point-in-time retrieval of regulatory text.** Regulators publish the current
rule; every audit, investigation and dispute is about a **past date**. Lex keeps
every version it has seen and answers *"what did this say on 15 March 2022?"*
with the exact validity interval, the timeline, the instrument that changed it,
and a hashed provenance record — and an honest, machine-readable refusal when it
cannot know.

**[Live demo](https://law.soufien.lu)** ·
**[Ask the AI](https://law.soufien.lu)** ·
**[MCP endpoint](https://law.soufien.lu/mcp)** ·
**[Dataset (CC-BY)](https://github.com/SFHAJJI/lex-articles)** ·
**[Examples](https://github.com/SFHAJJI/lex-articles/tree/main/examples)** ·
**[Architecture](https://law.soufien.lu/architecture)** ·
**[Verify it yourself](https://law.soufien.lu/verify)** ·
**[Spec (D1–D48)](docs/lex-spec-v4.md)**

## Try it in 30 seconds

Give any MCP-capable AI the full toolset — no key, no install:

```
claude mcp add --transport http lex https://law.soufien.lu/mcp
```

Or ask the [live site](https://law.soufien.lu). A real answer, verbatim:

> **Q: What did CRR Article 92 require as capital ratios on 1 March 2020 — and has that text changed since?**
>
> Quoted verbatim (Article 92(1)) from the CRR version in force on that date:
> *"Subject to Articles 93 and 94, institutions shall at all times satisfy the
> following own funds requirements: (a) a Common Equity Tier 1 capital ratio of
> 4,5 %; (b) a Tier 1 capital ratio of 6 %; (c) a total capital ratio of 8 %."*
> — `eu-eurlex:32013r0575:2019-12-25` (valid 2019-12-25 → 2020-06-26),
> [permalink](https://law.soufien.lu/eu-eurlex/32013r0575/2019-12-25#art_92).
>
> Article 92 has had **four distinct texts** since 2013 — 2013-06-28 → 2021-06-28,
> 2021-06-29 → 2022-12-31, 2023-01-01 → 2024-12-31, 2025-01-01 onward — each with
> its own permalink and sha256.

Every claim in that answer came from a deterministic tool call (the trace is
shown under each reply); the model never answers from its own memory.
Do not take this file's word for it — the numbers above are checkable in one call,
and if they ever drift from the live system, that is a bug worth reporting:

```bash
curl -s -X POST https://law.soufien.lu/mcp -H 'Content-Type: application/json' \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"article_history",
       "arguments":{"work":"eu-eurlex:32013r0575","anchor":"art_92"}}}'
```

## Who uses this

- **A compliance officer** checking which text of an obligation was in force on
  the date of the facts — with a permalink and a hash for the file.
- **A legal-tech developer** building RAG over law that must not hallucinate
  versions: per-article chunks with `valid_from`/`valid_to` to filter *before*
  similarity ([dataset](https://github.com/SFHAJJI/lex-articles)).
- **An AI agent** using the 9 MCP tools directly — the same tools the site's
  own AI uses, at the same endpoint.
- **A researcher** tracking how one article's text evolved across amendments
  (`article_history`: every distinct text state, dated).

## What it never does

Lex answers *what the rule was*. It never answers "were we compliant?", "does
this apply to me?", or "what does this mean?" — those are professional opinions.
No component in this system generates interpretive text (fitness rule F10).

## Architecture (one screen)

```
APPS        Lex.Ingest (CLI)   Lex.Mcp (MCP server, 9 tools)   Lex.Web (demo)   Lex.Ask (AI loop)
DERIVED     Lex.Derive — evidence -> per-article Markdown+JSON (immutable profiles: akn-lu/1, fmx4-eu/1, xhtml-eu/1)
ADAPTERS    Lex.Sources.Legilux (Tier A, SPARQL)   Lex.Sources.EurLex (Tier A, Cellar + Formex)
MODEL       Lex.Law — Publisher, Work, Version, Expression, Observation. No publisher names.
FOUNDATION  Lex.Temporal (interval algebra)   Lex.Index (SQLite: filter-first, signed stamp)
```

- **One corpus repo per publisher**; the corpus is human-readable JSON + git —
  `git log` *is* the legislative history.
- **Bitemporal**: valid time is the publisher's; transaction time is ours, as
  append-only observation chains inside hashed content. Nothing is ever
  overwritten; publisher corrections become visible events.
- **Two layers**: verbatim publisher bytes (evidence) → deterministic
  per-article extraction (consumption). Every derived article hash-chains to
  the exact bytes the state published; `lex verify derive` re-derives and
  byte-compares.
- **Filters before ranking, always** — enforced by construction (a non-optional
  `FilterSet` on the only query entry point).
- **Signed index stamps** (ECDSA-P256): every served hash is attributable.
- **Honest refusals**: `no_version_for_date`, `anchor_not_in_version`,
  `outside_observed_window`, `text_withheld` — a flagged wrong answer is still
  a wrong answer, so Lex refuses instead.

## Current coverage

**Luxembourg** (Legilux, Tier A): 1,399 works / 4,632 consolidated versions,
1849→2030, **full text** — verbatim Akoma Ntoso XML from the publisher's
official, robots-permitted filestore, licensed CC-BY-4.0 by the publisher.
Honest coverage claim: *dense and reliable from 2017 onward; real but sparse
before; isolated snapshots back to 1849; forward to 2030.*
**EU** (EUR-Lex/Cellar, Tier A): 8 flagship acts (GDPR, DORA, AI Act, NIS2,
MiFID II, CRR, PSD2, SFDR), 46 consolidated versions — full text from the
Publications Office's **Formex 4** structural XML where served (44/46),
including the large CRR consolidations the XHTML channel couldn't carry.
Derived layer: **1,212 works · 88,981 articles · 102,773 dated text states**.
The never-consolidated LU acts (~24,579) and the wider EU acquis are staged
next (spec §14).

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

## MCP tools

`as_of` (full / outline / per-article select) · `timeline` · `in_force_on` ·
`diff` · `search` · `provenance` · `article_history` · `changes_in_period` ·
`coverage` — `changes_in_period` answers across the corpus ("which laws moved
most in this window"), the aggregate counterpart of `diff` and `timeline`; and
`coverage` exists to say what we do **not** have, because a system that cannot
state its own gaps cannot be trusted with a completeness question.

## Contributing

Issues and PRs welcome — the highest-leverage areas:

- **A new publisher adapter** (`ISourceAdapter`, ~200 lines): any jurisdiction
  with an official machine-readable channel. The seam is publisher-pure by
  fitness test; adapters never touch files or git.
- **Eval cases** ([evals/cases.json](evals/cases.json)): questions where the AI
  should construct better tool calls — or refuse better.
- **Extraction improvements**: profiles are immutable; improvements ship as a
  *new* profile beside the old (see `fmx4-eu/1` beside `xhtml-eu/1`).

Contributions are accepted under the Developer Certificate of Origin
(`git commit -s`).

## Licence

Code: **Apache-2.0** ([LICENSE](LICENSE)). The code licence does **not** extend
to corpus data or index artefacts — see each corpus repository's `NOTICE`
(three layers: official acts outside copyright / Lex's compilation rights /
code licence inapplicable). Derived dataset: CC-BY-4.0 (LU) and EU
reuse-with-attribution, licence inline in every file.
