# Luxembourg corpus scope

Status: consolidation catalogue complete; original-act expansion planned

Measured: 2026-08-06 against the official Legilux SPARQL endpoint

## What is shipped

The Luxembourg adapter currently ingests the publisher's `jolux:Consolidation`
catalogue. The official endpoint returned 1,399 works and 4,638 current
consolidation records. The corpus contained every returned current record.
Archived records that disappear from a later publisher enumeration are retained
as evidence and marked `withdrawn_from_source`; they are not returned by public
search unless a provenance-specific lookup asks for them.

This is complete coverage of the consolidation catalogue, not complete coverage
of all Luxembourg legislation.

## Measured broader catalogue

The same endpoint returned these broad resource counts:

| Legilux class | Resources |
|---|---:|
| `Work` | 383,600 |
| `LegalResource` | 231,593 |
| `NationalLegalResource` | 226,158 |
| `Act` | 150,187 |
| `Consolidation` | 4,638 |

The `Act` class is not itself a safe ingestion boundary. It includes normative
instruments as well as notices, administrative publications and document types
that need different date or text semantics. The largest observed act-form
counts included PA 42,832, RC 33,009, RGD 15,354, AMIN 12,535, AGD 9,739, LOI
9,251, A 7,261, RMIN 6,090 and DIV 3,592.

The current consolidated works were concentrated in RGD 632 and LOI 568, with
smaller groups such as RECUEIL 38, AGD 28, RMIN 18 and AMIN 10. This confirms a
material lawyer-facing gap among original acts that never entered the
consolidation catalogue.

## Proposed next Luxembourg increment

The next scope review should select normative national forms such as LOI, RGD,
AMIN, AGD and RMIN, then measure publication date, entry-into-force data,
repeal signals, manifestations and relationship links per class. The review must
exclude notices and non-normative material deliberately rather than treating all
150,187 `Act` resources as equivalent law.

This increment requires implementation work:

1. Add an `Act` enumeration and identity adapter alongside `Consolidation`.
2. Define temporal semantics for original-only acts without inventing validity.
3. Resolve official XML, PDF and gazette manifestations with explicit extraction
   profiles.
4. Link an original act to a later consolidation without duplicating search
   states or timelines.
5. Add fixtures, scope preview counts, corpus validation and retrieval tests for
   each admitted document class.

Once those document-class semantics exist, adding further accepted national act
forms can become configuration-led. Until then, describing Luxembourg expansion
as a configuration-only change would be inaccurate.

## Product disclosure

The coverage page must continue to distinguish:

- complete coverage of the mounted consolidation catalogue;
- wording unavailable because the publisher supplies no admissible structural
  manifestation;
- original acts outside the current adapter and reviewed search scope; and
- deliberate exclusions such as communal regulations.

No total is presented as "all Luxembourg law" without naming the catalogue and
document classes from which it was computed.
