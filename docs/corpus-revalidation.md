# Corpus revalidation contract

Status: accepted V3 contract. Phase 0 and corrective Rebuild 0 must implement and measure every
gate below before Lex describes the corpus as continuously revalidated.

Lex stores observations of official publisher records. It does not infer that a law disappeared
because one request or one enumeration omitted it. Revalidation therefore has three independent
cadences and a fail-closed completion rule.

## Cadences

- Nightly: process the publisher feed, then enumerate every open state and every future-dated state.
- Weekly: enumerate the complete reviewed Luxembourg and EU catalogs.
- Monthly: issue a conditional request for every held manifestation whose official URI still resolves.

Manifestation and page retrieval uses GET only. It never uses HEAD. When a publisher supplied an
ETag, Lex sends `If-None-Match`; otherwise it uses a valid Last-Modified value with
`If-Modified-Since`. A 304 is a completed revalidation and keeps the prior bytes as the current
observation. A 200 response is preserved as received before decoding and compared by SHA-256.
These semantics follow [RFC 9110](https://www.rfc-editor.org/rfc/rfc9110.html#section-13.1.2).

Publisher access remains paced at a minimum of 1,500 ms between file requests. Legilux file and
page requests use `https://legilux.public.lu/filestore/` for XML, PDF and HTML only. Luxembourg
catalog queries use the official SPARQL protocol endpoint. EU enumeration and metadata use the
[Publications Office SPARQL endpoint](https://publications.europa.eu/webapi/rdf/sparql).

## Completed runs and absence

Every enumeration has an exact, bounded run identity. A retry with the same identity is the same
run and cannot advance an absence sequence. A run is completed only after the expected catalog
population, pagination and publisher-specific truncation checks pass. Every Cellar enumeration,
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

A corpus generation is ineligible for derivation, indexing or release if a required enumeration
is incomplete, its run identity is missing or repeated in an advancing absence sequence, a
publisher result violates its identity contract, a retained baseline changes during acquisition,
or any required revalidation metric is missing. Exit status alone is not evidence: the signed
manifest and the read-back of the resulting generation are the gate.
