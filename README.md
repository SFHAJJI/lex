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
**[Architecture dossier](https://law.soufien.lu/built)** ·
**[Benchmarks](https://law.soufien.lu/benchmarks)** ·
**[Verify it yourself](https://law.soufien.lu/verify)** ·
**[Program](docs/hybrid-eu-roadmap.md)** ·
**[Retrieval + agent plan](docs/retrieval-agent-enrichment-plan.md)** ·
**[Spec (D1-D82)](docs/lex-spec-v4.md)** ·
**[Corpus revalidation](docs/corpus-revalidation.md)** ·
**[Snapshot retention](docs/snapshot-retention.md)**

## 74-second engineering demo

[![Lex live temporal search, hybrid retrieval, comparison and deployment evidence](https://github.com/SFHAJJI/lex/releases/download/v1.2.1/lex-interviewer-preview-v2.gif)](https://github.com/SFHAJJI/lex/releases/download/v1.2.1/law-soufien-interviewer-demo-v2.mp4)

*Dated retrieval → deterministic keyword or optional local hybrid search → exact
EU article → verified temporal diff → evidence export → deployed architecture.*
[Watch the narrated, continuous-browser MP4](https://github.com/SFHAJJI/lex/releases/download/v1.2.1/law-soufien-interviewer-demo-v2.mp4)
or read the [release evidence](https://github.com/SFHAJJI/lex/releases/tag/v1.2.1).

## Try it in 30 seconds

Give any MCP-capable AI the full toolset, no key, no install:

```
claude mcp add --transport http lex https://law.soufien.lu/mcp
```

Modern clients such as VS Code and Cursor connect to the hosted endpoint directly:

```json
{ "servers": { "lex": { "type": "http", "url": "https://law.soufien.lu/mcp" } } }
```

For a client that only accepts local stdio servers, bridge to the same hosted
endpoint with a pinned version of the third-party `mcp-remote` adapter (Node.js 18+):

```
npx -y mcp-remote@0.1.38 https://law.soufien.lu/mcp
```

The hosted endpoint is canonical: no legal corpus, vector files or Azure
credentials are downloaded to the client. Lex also publishes its remote-server
metadata to the official MCP Registry from GitHub Actions using OIDC. Lex
intentionally does not publish an npm package: the command above is a
compatibility bridge for older clients, not a second implementation.

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
> Article 92 has had **seven distinct texts**: 2013-06-26 → 2013-06-27,
> 2013-06-28 → 2019-12-24, 2019-12-25 → 2020-06-26, 2020-06-27 → 2021-06-28,
> 2021-06-29 → 2022-12-31, 2023-01-01 → 2024-12-31 and 2025-01-01 onward, each
> with its own permalink and sha256.

Every claim in that answer came from a deterministic tool call (the trace is
shown under each reply); the model never answers from its own memory.
Do not take this file's word for it, the numbers above are checkable in one call,
and if they ever drift from the live system, that is a bug worth reporting:

```bash
curl -s -X POST https://law.soufien.lu/mcp -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
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
APPS        Lex.Ingest (CLI)   Lex.Mcp.Stdio (local host)   Lex.Web (site + HTTP MCP)   Lex.Ask (AI loop)
PROTOCOL    Lex.Mcp (legal tools + official MCP SDK bridge; transport-neutral library)
DERIVED     Lex.Derive, evidence -> provision Markdown+JSON (immutable profiles include akn-lu/1, akn-lu/2, akn-lu-identical-scl-duplicate/1, akn-lu-document/1, pdf-memorial-lu/2, fmx4-eu/1, xhtml-eu/1)
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
- **Legal and temporal eligibility before fusion and final ranking**, enforced by
  a non-optional `FilterSet` on the only query entry point. Hybrid may use a
  bounded binary-vector preselection for speed, but an ineligible candidate can
  never enter the fused result set.
- **Signed whole-artifact manifests** (ECDSA P-256): a trust root pinned in the
  application verifies indexes, vectors, embedding assets, scope, benchmark
  and source commits before any file is mounted. The embedded index stamp
  remains public provenance, not its own trust root.
- **Release-gated assistant behavior**: 25 frozen scenarios validate the typed
  plan, arguments, outcomes, UI effects, answer and latency against an immutable
  zero-traffic candidate. The catalog records author `Lex release engineering`;
  Evaluation reviewer Soufien Hajji uses a separate signing authority. This
  project-owner review is not a third-party audit. Candidate and release-grader tokens are budgeted
  separately. The current maximum reservation is EUR 0.5362647 under an outer
  EUR 10 preflight and measured-use ceiling, not a live billing cutoff. The
  [release dossier](https://law.soufien.lu/built/release) shows the CI/CD flow and
  [evaluation mechanics](docs/assistant-evaluation.md).
- **Honest refusals**: `no_version_for_date`, `anchor_not_in_version`,
  `outside_observed_window`, `text_not_available`, `text_withheld`, a flagged wrong answer is still
  a wrong answer, so Lex refuses instead.

## Current coverage

**Luxembourg** (Legilux, Tier A): every work and dated version currently mounted
from the publisher's `Consolidation` catalogue. Nothing in that collection is
filtered out by legal form. Counts, dates, corpus commit and extraction-profile
mix are read from the index on the
[live coverage page](https://law.soufien.lu/coverage), rather than copied into
product prose that becomes stale after the next publisher run.

The consolidation catalogue is not all Luxembourg law. The same official
endpoint exposes 150,187 resources classified as `Act`, including laws,
grand-ducal regulations, ministerial regulations and orders that may never have
received a consolidation record. That broad number also contains notices and
other material that should not all enter lawyer-facing search. The measured
boundary and the proposed normative-act increment are documented in
[Luxembourg scope](docs/luxembourg-scope.md).

Where official XML exists, text is retained as verbatim Akoma Ntoso. The
deterministic `pdf-lu/1` fallback handles eligible born-digital consolidated
PDFs and records that article boundaries came from typography rather than
publisher markup. Narrow `pdf-memorial-lu/2` recovery first verifies the requested
act inside an official-gazette issue, then exposes only a strongly identified
section and visibly labels its inferred boundaries in the reader. Thematic
folders, unverified gazette matches and fileless records remain metadata-only;
Lex does not trade provenance for a larger text count. Exact text availability
and extraction-profile mix are reported from the mounted artifact on the
coverage page.

**EU** (EUR-Lex/Cellar, Tier A): a reviewed Luxembourg-facing scope spanning
financial services, AML, corporate, competition, tax, employment, consumer,
procurement, environmental, judicial-cooperation, intellectual-property, data,
digital, cyber and energy law, plus bounded legal-history relationships. The mounted index and
[live coverage page](https://law.soufien.lu/coverage) are the source of truth for
work and version counts. Full text comes from the Publications Office's
**Formex 4** structural XML where served, including large consolidations the
XHTML channel cannot carry. The present EU limit is scope, not format.

The derived dataset publishes its current counts and source commits in its
[release catalog](https://github.com/SFHAJJI/lex-articles/blob/main/catalog.json).
The broader Luxembourg original-act catalogue and approved EU scope are tracked
by the [temporal expansion program](docs/hybrid-eu-roadmap.md). Communal regulations are deliberately out of scope: 17,232
exist as published acts, none is ever consolidated, so there is no point-in-time
history to hold. The fallback ladder for XML-less versions is spec D49.

## Run it

```
LEX_CODE_COMMIT=$(git rev-parse HEAD)
LEX_ARTICLES_COMMIT=$(git -C ../lex-articles rev-parse HEAD)
LEX_LU_CORPUS_COMMIT=$(git -C ../lex-corpus-lu-legilux rev-parse HEAD)
LEX_EU_CORPUS_COMMIT=$(git -C ../lex-corpus-eu-eurlex rev-parse HEAD)
# One exact completed-enumeration identity. Reuse it only when retrying that same run.
LEX_INGEST_RUN_ID=manual-example-001

# ingest (paced, sequential; official open-data channels only)
dotnet run --project src/Lex.Ingest -- ingest --publisher lu-legilux \
    --corpus ../lex-corpus-lu-legilux --code-commit "$LEX_CODE_COMMIT" \
    --run-id "$LEX_INGEST_RUN_ID"

# derive the per-article layer, build the signed index
dotnet run --project src/Lex.Ingest -- derive --publisher lu-legilux --corpus ../lex-corpus-lu-legilux --out ../lex-articles
dotnet run --project src/Lex.Ingest -- index --corpus ../lex-corpus-lu-legilux --articles ../lex-articles \
    --out indexes/index-lu-legilux.db --keyfile signing-key.pem \
    --code-commit "$LEX_CODE_COMMIT" --articles-commit "$LEX_ARTICLES_COMMIT" \
    --corpus-commit "$LEX_LU_CORPUS_COMMIT"

# resumable large semantic backfill on a reviewed Windows DirectML adapter
dotnet build src/Lex.Ingest -c Release -p:UseDirectML=true
src/Lex.Ingest/bin/Release/net10.0/Lex.Ingest index \
    --corpus ../lex-corpus-eu-eurlex --articles ../lex-articles \
    --out indexes/index-eu-eurlex.db --embedding-model model \
    --vectors indexes/index-eu-eurlex.vectors \
    --embedding-directml-device 1 --embedding-batch-size 256 \
    --embedding-max-batch-tokens 32768 \
    --embedding-cache build-cache/eu-eurlex-embeddings.db \
    --code-commit "$LEX_CODE_COMMIT" --articles-commit "$LEX_ARTICLES_COMMIT" \
    --corpus-commit "$LEX_EU_CORPUS_COMMIT"

# The chunker fixes legal-text boundaries before the GPU groups immutable chunks
# into 32/64/128/256/512-token inference buckets. A fixed padded-token budget reduces
# the item count for long buckets so one reviewed batch size cannot exhaust the GPU.
# Masked padding is never stored.

# web demo + MCP (stdio) locally
LEX_INDEX_DIR=indexes dotnet run --project src/Lex.Web
LEX_INDEX_DIR=indexes dotnet run --project src/Lex.Mcp.Stdio
```

`Lex.Mcp` contains the legal tools and official SDK bridge, not a deployment entry point.
The standalone stdio executable is isolated in `Lex.Mcp.Stdio`; production composes the same
library into `Lex.Web` for Streamable HTTP. Co-hosting is deliberate while site and MCP traffic
share one immutable index set and one scale/SLA boundary. D67 records the measured triggers for
extracting an independently deployed MCP service rather than adding a second runtime for optics.

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
retrieval. A unique segment of an official publisher short title such as `RGPD`,
`GDPR`, `DORA`, or `AI Act` resolves deterministically; collisions require
clarification. Official publisher subjects, EuroVoc relations, and directory
coordinates support weak discovery but never become legal-text evidence or work
identity. No manually curated legal aliases are loaded. Model-derived weak discovery is
not active, and keyword remains the production default because the signed hybrid
holdout gate has not passed. The optional assistant runs a bounded retrieval loop over
the same tools, then uses Agent Framework for claim-typed composition and a conditional
grounding judge. Application code retains work resolution, tool authorization, citation
and gap authority. `coverage` exists to say what Lex does **not** have, because a system
that cannot state its own gaps cannot be trusted with a completeness question.

## Contributing

Issues and PRs welcome, the highest-leverage areas:

- **A new publisher adapter** (`ISourceAdapter`, ~200 lines): any jurisdiction
  with an official machine-readable channel. The seam is publisher-pure by
  fitness test; adapters never touch files or git.
- **Assistant release cases** ([evals/assistant-cases-v3.json](evals/assistant-cases-v3.json)):
  frozen typed-operation judgments, digest-attested by a project-owner reviewer identity distinct
  from the catalog author and run with the strict
  [release evaluator](docs/assistant-evaluation.md). The gate has no keyword or grader fallback;
  cases specify the exact operation contract or refusal boundary expected from a natural question.
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
