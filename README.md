# Lex

**Point-in-time retrieval of regulatory text.** Regulators publish the current
rule; every audit, investigation and dispute is about a **past date**. Lex keeps
every version it has seen and answers *"what did this say on 15 March 2022?"*
with the exact validity interval, the timeline, the instrument that changed it,
and a hashed provenance record — and an honest, machine-readable refusal when it
cannot know.

**Live:** https://law.soufien.lu — AI answers grounded in signed per-article
indexes (the front page), permalinks/timelines/diffs, a hosted MCP endpoint
(`/mcp`, 8 tools incl. `article_history`), and a
[verify-it-yourself](https://law.soufien.lu/verify) auditor surface.
**Machine-readable data:** [lex-articles](https://github.com/SFHAJJI/lex-articles)
— per-provision Markdown+JSON, point-in-time, hash-chained (CC-BY).
**Architecture in one page:** https://law.soufien.lu/architecture ·
**Specification:** [docs/lex-spec-v4.md](docs/lex-spec-v4.md) — the full
decision record (D1–D47), fitness rules and risk register.

## What it never does

Lex answers *what the rule was*. It never answers "were we compliant?", "does
this apply to me?", or "what does this mean?" — those are professional opinions.
No component in this system generates interpretive text (fitness rule F10).

## Architecture (one screen)

```
APPS        Lex.Ingest (CLI)   Lex.Mcp (MCP server, 7 tools)   Lex.Web (public demo)
ADAPTERS    Lex.Sources.Legilux (Tier A, SPARQL)   Lex.Sources.EurLex (Tier A)
MODEL       Lex.Law — Publisher, Work, Version, Expression, Observation. No publisher names.
FOUNDATION  Lex.Temporal (interval algebra)   Lex.Index (SQLite: filter-first, signed stamp)
```

- **One corpus repo per publisher**; the corpus is human-readable JSON + git —
  `git log` *is* the legislative history.
- **Bitemporal**: valid time is the publisher's; transaction time is ours, as
  append-only observation chains inside hashed content. Nothing is ever
  overwritten; publisher corrections become visible events.
- **Filters before ranking, always** — enforced by construction (a non-optional
  `FilterSet` on the only query entry point).
- **Signed index stamps** (ECDSA-P256): every served hash is attributable.
- **Honest refusals**: `no_version_for_date`, `outside_observed_window`,
  `text_withheld` — a flagged wrong answer is still a wrong answer, so Lex
  refuses instead.

## Current coverage

Luxembourg (Legilux, Tier A): 1,399 works / 4,644 consolidated versions,
1849→2030, **full text** — verbatim Akoma Ntoso XML from the publisher's
official, robots-permitted filestore, licensed CC-BY-4.0 by the publisher
(spec D44). Honest coverage claim: *dense and reliable from 2017 onward; real
but sparse before; isolated snapshots back to 1849; forward to 2030.*
EU (EUR-Lex, Tier A): 8 flagship acts (GDPR, DORA, AI Act, NIS2, MiFID II,
CRR, PSD2, SFDR), 46 consolidated versions with verbatim XHTML text where the
publisher serves it. The never-consolidated LU acts (~24,579) and the wider EU
acquis (75,019 consolidated versions) are staged next (spec §14).

## Run it

```
# ingest (paced, sequential; the endpoint is the publisher's official open-data channel)
dotnet run --project src/Lex.Ingest -- ingest --publisher lu-legilux --corpus ../lex-corpus-lu-legilux

# build the signed index
dotnet run --project src/Lex.Ingest -- index --corpus ../lex-corpus-lu-legilux \
    --out indexes/index-lu-legilux.db --keyfile ../lex-ops/signing-key.pem

# web demo
LEX_INDEX_DIR=indexes dotnet run --project src/Lex.Web

# MCP server (stdio) — plug into Claude Code / any MCP client
LEX_INDEX_DIR=indexes dotnet run --project src/Lex.Mcp
```

## MCP tools

`as_of` · `timeline` · `in_force_on` · `diff` · `search` · `provenance` ·
`coverage` — coverage exists to say what we do **not** have; a system that
cannot state its own gaps cannot be trusted with a completeness question.

Hosted endpoint (Streamable HTTP, no key needed):

```
claude mcp add --transport http lex https://law.soufien.lu/mcp
```

## Licence

Code: **Apache-2.0** ([LICENSE](LICENSE)). The code licence does **not** extend
to corpus data or index artefacts — see each corpus repository's `NOTICE`
(three layers: official acts outside copyright / Lex's compilation rights /
code licence inapplicable). Contribution: DCO.
