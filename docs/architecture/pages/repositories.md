# Repositories

Lex is five published repositories, not one. The split is deliberate: publisher evidence, the
derived dataset, the product and the authority that signs releases have different lifetimes,
different licences and different people who may write to them.

## What is published

| Repository | Holds | Role |
|---|---|---|
| `lex` | The applications (`Lex.Ingest`, `Lex.Mcp.Stdio`, `Lex.Web`, `Lex.Ask`), the legal model, the index reader, the MCP tool core, the site, the golden suite, the spec and this dossier. | The product. Everything here is code and documentation; it contains no law and no index. |
| `lex-corpus-lu-legilux` | Luxembourg publisher evidence and the provisions derived from it, as human-readable JSON under `works/`, plus a `manifest.json`. | One corpus repository per publisher. The tree carries the legislative history and `git log` carries the ingest history, deliberately not the same axis. |
| `lex-corpus-eu-eurlex` | The same structure for EUR-Lex. | Keeping publishers apart means a bad ingest from one can never rewrite the other, and each carries its own licence and attribution. |
| `lex-articles` | The per-article dataset with `catalog.json`, `generation.json`, a schema and worked examples. | The consumable output, published under CC-BY for retrieval systems that must filter by validity before similarity. |
| `lex-ops` | Publication workflows, the fleet scripts and the assistant evaluation publisher. | Release authority, held apart from the product it releases. The signing key lives here on disk and is never committed. |

## Why the product repository holds no data

The corpus is evidence and the index is a build output, so neither belongs beside the code that
reads them. The index is roughly 947 MB, is gitignored, and is baked into the container image at
build time. A container built from this repository alone therefore mounts zero indexes and must
answer `no_corpus_mounted` rather than an empty list, which is the difference between saying
nothing is held and saying nothing exists.

## Working directories that are not repositories

Several sibling directories look like repositories and are not. Four are git worktrees of `lex`
checked out on feature branches, so their contents are branches of the product, not separate
history. Two more are local build outputs with no remote: one holds a built index database, the
other holds compressed provision exports. None of the six is published, and none should be cited
as a source.

## Separation of authority

The evaluation that gates a release is authored in `lex` and published from `lex-ops`, by a
different identity, against a catalog whose exact bytes the project owner has signed. That is
separation from the catalog author, not third-party review or external audit, and the
[release page](/built/release) states it in those terms. The same boundary is why the signing key
sits in the operations repository and the product repository can neither read nor produce a
signature.
