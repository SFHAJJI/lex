# Overview

Lex answers a narrow business question: **what did this rule say on this date?** It turns dated
Luxembourg and EU publisher material into article-level research with an official link, an exact
version and a verifiable evidence chain. It does not decide legal applicability or advise a reader
what to do.

![Official publishers flow through a deterministic corpus and signed index into one legal core used by the web app, public MCP server and bounded assistant.](/built/diagrams/system.svg)

[Open the system diagram at full size](/built/diagrams/system.svg)

| Box | Responsibility | Lives in |
|---|---|---|
| Official publishers | Authoritative catalogues, classifications and source files | Legilux and EUR-Lex/Cellar |
| Evidence and derivation | Preserve publisher bytes, then extract articles or typed gaps reproducibly | `lex-corpus-*`, `Lex.Sources.*` and `Lex.Derive` |
| Signed index | Bind time, text, metadata, vectors and provenance into local immutable artifacts | `Lex.Index`, built by `Lex.Ingest` |
| Legal core | Execute the same closed read-only operations for every channel | `Lex.Mcp` |
| Bounded agent | Plan against closed schemas and optionally explain an accepted evidence ledger | `Lex.Ask` |
| Reader channels | Render the workspace and expose HTTP or stdio MCP without duplicating legal logic | `Lex.Web` and `Lex.Mcp.Stdio` |

## The decision a reader can make

| Reader need | Product behavior | Failure behavior |
|---|---|---|
| Find the right instrument | Resolve official identity before searching article text | Show several candidates or ask for clarification |
| Read it at a date | Select the publisher state whose closed interval contains that date | Return a typed gap, never substitute today's text |
| Understand why it matched | Return the matched title, identifier or official classification | Never present metadata as wording from the law |
| Compare or explain | Execute typed legal operations first | Keep deterministic results when prose is not requested |
| Verify a claim | Return publisher permalinks, version coordinates and provision hashes | Refuse unsupported model-written claims |

## The architecture in one sentence

Official publisher records become immutable evidence, deterministic derivations and signed local
indexes; one typed legal core serves the UI, MCP and a bounded agent whose model may plan and
explain but cannot choose legal identity, dates, evidence or actions.

## How to read this dossier

The first four tabs explain the legal and retrieval product. **Assistant** shows where the agent is
useful and where it has no authority. **Release** shows how evaluated code and data become one
rollback-safe revision. The final three tabs expose decisions, incidents and limits instead of
hiding them behind a feature list.

The implementation described here is merged and gated for a fresh signed v4 corpus, candidate
evaluation and promotion. Post-promotion latency, relevance, coverage and memory figures are
therefore marked pending until they can be read from that exact revision or its signed reports.

## Why this is more than a naive RAG

Lex is retrieval-augmented generation only when the reader asks for prose. Identity resolution,
point-in-time selection, retrieval, comparison, citations and direct result cards work without
generation. The default result is deterministic. Optional prose receives an already bounded
evidence ledger and must bind each claim back to that ledger.
