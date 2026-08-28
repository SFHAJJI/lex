# Snapshot retention and replay contract

Status: accepted V3 contract. Promotion may claim replay only after the signed inventory,
selection ledger, immutable assets and cleanup guards below have passed live read-back.

Lex replay means deterministic operations against an exact retained signed snapshot. It covers
typed plans, verdicts, claim graphs, evidence objects and deterministic tool results. Unsigned generated prose is not replayable and is never part of a signed evidence bundle.

## Retention classes

- Every complete LU and EU index release referenced by an issued evidence bundle is retained
  indefinitely.
- Nightly releases are retained for 90 days unless another rule retains them longer.
- One eligible complete LU and EU release from each UTC month is retained indefinitely as that
  month's keeper.

An eligible monthly release has both publisher artifacts, valid signatures, a complete retention
receipt, a capability manifest, an evaluation identity and the exact corpus, derivation, builder,
canon and toolchain identities needed for read-back.

After a UTC month closes, the eligible release whose signed retention receipt was appended latest in that UTC month becomes the keeper. If receipt timestamps tie, the lexicographically smallest manifest-set identifier wins. If no release is eligible, the signed ledger appends
`no_eligible_release`. A recorded keeper selection cannot be replaced or deleted.

## Bound identity

Every retained release records immutable LU and EU release tags and asset names, SHA-256 hashes,
detached signatures, corpus commits, corpus-manifest hashes, derivation commits, builder commit,
index format, capability-manifest hash, evaluation identity, canon ID, SDK, runtime and locked
dependency identity. An evidence bundle embeds the exact retained release and manifest-set
identifiers it needs.

GitHub supports protected immutable tags and release assets, plus release attestations. The
repository must verify that setting and each published asset through the official
[immutable releases contract](https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases)
before treating GitHub immutability as evidence. A normal editable release is not sufficient.

## Inventory and replay

The public machine-readable retention inventory states each snapshot's retention class and
reason, build time, manifest-set identifier, signature status, oldest replayable observation date
and last independent audit time. The client-side runbook downloads the exact public assets,
verifies every hash and signature, restores the bound code/runtime identity and executes the
recorded typed operation against that snapshot.

The inventory and coverage surface state exactly:

> Observation history begins August 2026; replay depth grows from here.

This is a lower bound on Lex's observation history, not a claim about the publisher's historical
holdings.

## Cleanup and failure behavior

Automated cleanup fails closed if an artifact is referenced by an evidence bundle, selected as a
monthly keeper, protected by the current production or rollback identity, missing from the signed
ledger, or not provably older than the 90-day nightly window. Deletion planning uses immutable
digests and exact asset identities, never tags alone, names alone or age alone.

The selection ledger is append-only and signed under its truthful technical identity. Cleanup
recomputes and reads back the live inventory immediately before any mutation. A missing or
inconsistent receipt produces no deletion. Release assets remain subject to GitHub's documented
per-file limit, currently 2 GiB, so index sharding is decided before corpus expansion approaches
that boundary. See [GitHub release limits](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases#storage-and-bandwidth-quotas).

Storage cost and release frequency are measured facts. If indefinite evidence retention proves
unaffordable, Lex removes the word `replay` from product claims and publishes the narrower
retained-snapshot guarantee. It does not silently weaken this contract.
