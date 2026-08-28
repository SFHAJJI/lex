# Corpus revalidation contract

Status: accepted V3 contract. Phase 0 and corrective Rebuild 0 must implement and measure every
gate below before Lex describes the corpus as continuously revalidated.

The normative machine values are in [`truth-contract-v3.json`](truth-contract-v3.json). Its
`accepted_v3_target_not_deployed` status is a target contract, not evidence of live enforcement.
Any consumer must validate its exact schema and values and reject missing, unknown or unrecognized
fields or values. Without successful live read-back, the continuous-revalidation claim is blocked.

Lex stores observations of official publisher records. It does not infer that a law disappeared
because one request or one enumeration omitted it. Revalidation therefore has three independent
cadences and a fail-closed completion rule.

## Cadences

- Nightly: process the publisher feed, then enumerate every open state and every future-dated state.
- Weekly: enumerate the complete reviewed Luxembourg and EU catalogs.
- Monthly: issue a conditional request for every held manifestation whose official URI still resolves.

Manifestation and page retrieval uses GET only. It never uses HEAD. When a publisher supplied an
ETag, Lex sends `If-None-Match`; otherwise it uses a valid Last-Modified value with
`If-Modified-Since`. If neither validator exists, Lex performs an unconditional GET; it never
substitutes HEAD. A 304 is a completed revalidation and keeps the prior bytes as the current
observation. A 200 response is preserved as received before decoding and compared by SHA-256.
These semantics follow RFC 9110 for
[`If-None-Match`](https://www.rfc-editor.org/rfc/rfc9110.html#section-13.1.2) and
[`If-Modified-Since`](https://www.rfc-editor.org/rfc/rfc9110.html#section-13.1.3).

Publisher access remains paced at a minimum of 1,500 ms between file requests. Legilux file and
page requests use `https://legilux.public.lu/filestore/` for XML, PDF and HTML only. Luxembourg
catalog queries use the official SPARQL protocol endpoint. EU enumeration and metadata use the
[Publications Office SPARQL endpoint](https://publications.europa.eu/webapi/rdf/sparql).

## Completed runs and absence

Every cadence execution has an exact logical run identity: the lowercase SHA-256 of an
[RFC 8785](https://www.rfc-editor.org/rfc/rfc8785.html) canonical JSON object containing only
`publisher`, `cadence`, `scheduled_slot_utc` and `scope_manifest_sha256`. Nightly slots are UTC
dates, weekly slots are ISO UTC weeks and monthly slots are UTC months. Attempt number, process
ID, wall-clock start and random values are excluded.
The scope manifest is frozen for that slot, and every retry must reuse the same identity. A retry
that changes either value is identity-incoherent, cannot complete and cannot advance absence.

A nightly run is incomplete unless feed processing and the open and future-state enumeration
both complete.
Every required component of all three cadences, not only catalog enumeration, must complete
before its logical run is completed. Completion also requires the expected target set, pagination,
publisher-specific truncation checks and identity checks to pass. Every Cellar enumeration,
including an evaluation oracle query, has a one-million-row truncation guard.

The first complete run that does not contain a previously held version appends
`absent_unconfirmed(first_missed_at, runs_missed=1, run_identity)`. A second distinct completed
run advances the count to two. Only three distinct completed run identities may append
`withdrawn_from_source(runs_missed=3)`. Re-sighting the version appends `resighted` and resets the
pending sequence without deleting its history.

No absence event is appended after a failed, truncated, duplicate or identity-incoherent run.
Those outcomes produce a bounded upstream-health record and leave the prior corpus generation
unchanged. The historical one-run withdrawals are not silently grandfathered. Each must receive
an append-only audit result based on new official observations before corrective Rebuild 0.

## Changed bytes and evidence

A changed body at the same official URI never overwrites held bytes. Lex stores a new
content-addressed observation and appends `file_replaced`, retaining the earlier byte object and
its source evidence. Historical bodies that were decoded before transport-byte preservation are
never relabeled as byte-verbatim after the fact.

Bounded HTTP evidence may contain status, content type, charset, ETag, Last-Modified, fetch time,
attempt count, requested official URI and effective official URI. It never contains cookies,
authorization values, arbitrary headers, request bodies, query text, IP addresses or user-agent
strings.

## Published measurements

The coverage surface reports, per publisher and cadence:

- rows expected, attempted and completed;
- successful 200 and 304 outcomes;
- bounded failure classes;
- elapsed fetch hours and the last completed run identity;
- first observation date and last successful cadence time.

Publisher-feed lag is a dated Lex measurement, never a publisher guarantee. Counts are computed
from the mounted signed artifact and carry its build date. Scheduling any large scope expansion
is conditional on the measured monthly revalidation budget.

## Release gate

A corpus generation is ineligible for derivation, indexing or release if a required cadence
component is incomplete, its run identity is missing or repeated in an advancing absence
sequence, a publisher result violates its identity contract, a retained baseline changes during
acquisition, or any required revalidation metric is missing. Exit status alone is not evidence:
the signed manifest and the read-back of the resulting generation are the gate.
