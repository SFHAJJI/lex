# Overview

Lex answers a narrow business question: **what did this rule say on this date?** It turns dated
Luxembourg and EU publisher material into article-level research with an official link, an exact
version and a verifiable evidence chain. It does not decide legal applicability or advise a reader
what to do.

![Three planes: a nightly data plane from publishers through an append-only corpus, gated derivation and index build to signed artifacts; a per-release plane from an immutable image through a zero-traffic candidate, gates and a signed evaluation to one promotion; a per-question runtime plane from reader through admission, subject preflight, planner, gate and freeze, executor and the legal core to a typed reply with an optional composer and judge.](/built/diagrams/three-planes.svg)

[Open the three-plane diagram at full size](/built/diagrams/three-planes.svg)

The whole system is three planes that meet at exactly two artifacts. The data plane runs nightly
and turns publisher bytes into an append-only corpus, deterministic derivations and a signed
index. The release plane runs per release and turns code plus verified indexes into one immutable
image that must pass its gates and a signed evaluation before a single promotion. The runtime
plane runs per question and answers from the signed index alone, resolving identity before any
model, freezing one typed plan, and returning typed, cited results. The planes never share state
except the two signed artifacts: the index manifest binds the data plane to the release plane,
and the evaluation report binds the release plane to the exact revision that serves.

| Plane | Cadence | Produces | Hands the next plane |
|---|---|---|---|
| Data | Nightly, per publisher | Append-only corpus, derived articles, SQLite index | Signed artifact manifests |
| Release | Per release | Immutable image, zero-traffic candidate, gates, signed evaluation | One promotion, rollback retained |
| Runtime | Per question | Identity in code, one frozen plan, typed cited reply, optional judged prose | Nothing; it only reads the two signed artifacts |

## Where each responsibility lives

| Box | Responsibility | Lives in |
|---|---|---|
| Official publishers | Authoritative catalogues, classifications and source files | Legilux and EUR-Lex/Cellar |
| Evidence and derivation | Preserve publisher bytes, then extract articles or typed gaps reproducibly | `lex-corpus-*`, `Lex.Sources.*`, `Lex.Law` and `Lex.Derive` |
| Signed index | Bind time, text, metadata, vectors and provenance into local immutable artifacts | `Lex.Index` and `Lex.Temporal` version identity, built by `Lex.Ingest` |
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
rollback-safe revision. The final four tabs expose decisions, incidents, limits and the repository
split instead of hiding them behind a feature list.

The pages describe durable contracts, not a moment in one rollout. Mounted corpus identities,
coverage and retrieval capabilities are read from the running revision, while latency, relevance,
memory and promotion claims appear only when a signed report binds them to that exact artifact set.

## Why this is more than a naive RAG

Lex is retrieval-augmented generation only when the reader asks for prose. Identity resolution,
point-in-time selection, retrieval, comparison, citations and direct result cards work without
generation. The default result is deterministic. Optional prose receives an already bounded
evidence ledger and must bind each claim back to that ledger.
