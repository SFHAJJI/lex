# Snapshot retention and replay contract

Status: accepted V3 contract. Promotion may claim replay only after the signed inventory,
selection ledger, immutable assets and cleanup guards below have passed live read-back.

The normative machine values are in [`truth-contract-v3.json`](truth-contract-v3.json). Its
`accepted_v3_target_not_deployed` status is a target contract, not evidence of live enforcement.
Any consumer must validate its exact schema and values and reject missing, unknown or unrecognized
fields or values. Without successful live read-back, replay promotion is blocked.

Lex replay means deterministic operations against an exact retained signed snapshot. It covers
typed plans, verdicts, claim graphs, evidence objects and deterministic tool results. Unsigned
generated prose is not replayable and is never part of a signed evidence bundle.

## Retention classes

- Every complete LU and EU index release referenced by an issued evidence bundle is retained
  indefinitely.
- Nightly releases are retained for 90 days unless another rule retains them longer.
- One eligible complete LU and EU release from each UTC month is retained indefinitely as that
  month's keeper.

The signed ledger appender and cleanup executor, not their callers, read the configured trusted UTC
clock. The appender supplies `accepted_at_utc` and rejects a timestamp older than the current ledger
head. The cleanup executor supplies `now_utc`; an unavailable or invalid clock produces no
deletion. Nightly retention means 2,160 elapsed hours from that accepted append time. Deletion is
eligible only when `now_utc >= accepted_at_utc + 2,160 hours`. At exactly 2,160 elapsed hours the
release becomes deletion-eligible; before that instant it remains protected. This is the
executable meaning of the 90-day window.

An eligible monthly release has both publisher artifacts, valid signatures, a complete retention
receipt, a capability manifest, an evaluation identity and the exact corpus, derivation, builder,
canon and toolchain identities needed for read-back.

After a UTC month closes, the eligible release whose signed retention receipt was appended latest
in that UTC month becomes the keeper. The ledger-supplied `accepted_at_utc` is that append time.
If accepted timestamps tie, the lexicographically smallest manifest-set identifier wins. If no
release is eligible, the signed ledger appends `no_eligible_release`. A `no_eligible_release`
result is final for that month and cannot later be replaced by a retroactively eligible release.
A recorded keeper selection cannot be replaced or deleted.

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

The public machine-readable retention inventory states each snapshot's retention class,
`reason_codes`, build time, manifest-set identifier, signature status, oldest replayable
observation date and last independent audit time. Public `reason_codes` is a closed, derived-only
enum and never free text.
Its only values are `evidence_bundle_reference`, `monthly_keeper`, `nightly_window`,
`current_production` and `current_rollback`. It never exposes a bundle identifier, bundle
narrative, query text, request body, IP address, user-agent, referrer, cookie or authorization
value.

The client-side runbook downloads the exact public assets,
verifies every hash and signature, restores the bound code/runtime identity and executes the
recorded typed operation against that snapshot.

Retention is necessary but not sufficient for a replay claim. Before authorization, replay also
requires all of the following:

- canonical plan and receipt recomputation;
- exact tool-source binding and path-specific schema validation;
- bounded streaming before allocation;
- strict transport validation and static-pin cross-checks;
- practical encoded-query leak detection that retains or prints match counts only;
- controlled malformed-set rejection;
- fresh validation with separately bounded age and duration;
- `not_assessed_no_specific_checks` for rows without semantic checks;
- `not_executed_as_contracted` for contracted non-execution, never a passing assertion; and
- at least one live probe repeated independently outside the receipt.

Failure or absence of any replay gate blocks the replay claim. Passing this retention contract
alone never authorizes it.

The inventory and coverage surface state exactly:

> Observation history begins August 2026; replay depth grows from here.

This is a lower bound on Lex's observation history, not a claim about the publisher's historical
holdings.

## Cleanup and failure behavior

Cleanup fails closed if an artifact is referenced by an evidence bundle, selected as a
monthly keeper, protected by the current production or rollback identity, missing from the signed
ledger, or not provably older than the 90-day nightly window. Deletion planning uses immutable
digests and exact asset identities, never tags alone, names alone or age alone.

The selection ledger is append-only and signed under its truthful technical identity.

Bundle-reference publication, monthly selection and cleanup share one cooperative lock named
`lex-retention-ledger/1`. Lock acquisition returns a fencing token. Every ledger append supplies
the expected signed ledger-head digest and succeeds only through compare-and-swap.

Bundle issuance appends and reads back its snapshot reference before the bundle is returned. It
does so while holding the lock, using the fencing token and expected-head compare-and-swap. A
missing asset, failed append, changed head or failed read-back means no bundle is issued.

Cleanup acquires the same lock, verifies the live inventory, and builds an exact digest-bound
plan. Each signed deletion authorization names exactly one immutable object. Cleanup appends and
reads back that authorization through compare-and-swap, then immediately verifies that its fencing
token is current and the ledger head is still that authorization before deleting exactly that
object. It appends and reads back the result through compare-and-swap. A multi-object plan repeats
that authorize, read-back, delete and record sequence for each object while retaining the lock.
Any mismatch causes no further mutation and requires reconciliation from the signed authorization
record.

Every automated, operator-workflow, API or UI cleanup deletion path uses that same lock and
ledger-head compare-and-swap protocol. Direct UI or API deletion that cannot participate must be
disabled before replay promotion. If any configured deletion path can bypass the protocol, replay
promotion is blocked. GitHub release immutability does not replace this gate because an entire
immutable release can still be deleted.

A missing or inconsistent receipt produces no deletion. Release assets remain subject to GitHub's
documented per-file limit, currently 2 GiB, so index sharding is decided before corpus expansion
approaches that boundary. See
[GitHub release limits](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases#storage-and-bandwidth-quotas).

Storage cost and release frequency are measured facts. If indefinite evidence retention proves
unaffordable, Lex removes the word `replay` from product claims and publishes the narrower
retained-snapshot guarantee. It does not silently weaken this contract.
