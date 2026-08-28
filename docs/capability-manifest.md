# Signed capability manifest

Status: V3 Phase 0 contract.

Lex must not answer an unsupported filter with a successful empty result. Index construction
therefore produces `lex-capability-manifest/1`, and the server checks it before executing a
governed query.

## Governed filters

The first schema governs `hierarchy`, `act_form`, `binding_status`, and `domain`. Each maps to one
stored document field. Adding another governed filter is an additive manifest schema change and
requires the same build and runtime gates.

## Population rows

The manifest is a deterministic SQLite table inside each publisher index. A row contains:

- filter name;
- language, using `*` for the publisher aggregate;
- time scope, either `all_versions` or `as_of`;
- an inclusive effective-period start and end for `as_of` rows;
- eligible non-withdrawn expression rows;
- rows whose governed field is non-null and non-blank.

The builder derives effective periods from the signed document intervals. A new period starts at
each `valid_from` and on the day after each finite `valid_to`. Adjacent periods with identical
counts may be merged. An open final period has no end. `all_versions` has one row per filter and
language and measures every non-withdrawn stored version.

An index with no eligible non-withdrawn expression rows has an empty table and stamps every
governed filter as unsupported. Runtime checks therefore fail closed without inventing a
population denominator.

A slice is supported only when it has at least one eligible row and every eligible row is
populated. Partial population is not advertised as support. This conservative rule is deliberate:
an empty result over a partially described slice cannot prove that the requested legal category
is absent.

Rows are canonical in ordinal filter, language, scope, start, and end order. The stamp carries the
manifest schema, row count, canonical SHA-256 digest, policy tier, and policy SHA-256. The existing
index signature therefore binds both the population evidence and the build policy. The reader
recomputes the digest and rejects missing, duplicate, out-of-bounds, or malformed signed claims.

## Two build gates

Thin test fixtures use a hand-written exact expected manifest. A mismatch fails the build, which
proves the gate itself rather than inheriting production assumptions.

Production uses the checked-in `deploy/capability-policy.json`. Its allowlist names the filters
expected to be unsupported in each publisher. Construction fails if an allowlisted filter gains
support, if any other governed filter lacks complete support, or if any language or effective-time
slice contradicts the field-level expectation. A data improvement must therefore change the data
and the reviewed allowlist together.

## Runtime behavior

The server evaluates the manifest before search:

- `search` with `all_versions` checks the aggregate row;
- `search` with `as_of` and `in_force_on` check the effective period containing the requested
  date;
- `changes_in_period` checks every `as_of` period that intersects the inclusive requested range
  and refuses when any intersecting slice is unsupported.

An unsupported request returns `filter_not_supported_by_index` before ranking or result SQL. The
payload names the unsupported filters, requested language and time scope, and the signed manifest
digest. With several mounted publishers, each unsupported publisher returns its own typed result;
supported publishers may still execute.

An older index without `lex-capability-manifest/1` is readable for migration, but every governed
filter fails closed. It can never regain the old `ok` plus empty-array behavior.

## Release evidence

Promotion evidence records the capability manifest digest and policy digest from every mounted
publisher. Candidate verification must show that the production unsupported set exactly matches
the checked-in policy and independently recompute the manifest from indexed document rows before
traffic can move.
