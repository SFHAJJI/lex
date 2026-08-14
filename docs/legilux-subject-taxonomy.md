# Luxembourg subject facets from the publisher's own taxonomy

Status: **captured for weak retrieval**. Taxonomy browsing and facets remain deferred.

## 1. What exists upstream

Legilux classifies its acts against a two-level SKOS authority scheme and publishes the
assertions in the same SPARQL dataset the adapter already queries.

```sparql
PREFIX jolux: <http://data.legilux.public.lu/resource/ontology/jolux#>
SELECT ?p (COUNT(*) AS ?n) WHERE {
  ?s a jolux:Act ; ?p ?o .
  FILTER(CONTAINS(LCASE(STR(?p)), "subject"))
} GROUP BY ?p ORDER BY DESC(?n)
```

| predicate | assertions |
|---|---|
| `jolux:subjectLevel2` | 254,716 |
| `jolux:subjectLevel1` | 149,933 |

Values are authority URIs under
`http://data.legilux.public.lu/resource/authority/legal-subject/{id}`, each carrying a
`skos:prefLabel`. The most-used level-1 subjects, by distinct acts:

| authority | label | acts |
|---|---|---|
| `legal-subject/918` | Règlement communal | 17,232 |
| `legal-subject/674` | activité sociale, familiale et thérapeutique | 13,963 |
| `legal-subject/728` | circulation routière | 3,965 |
| `legal-subject/259` | convention internationale | 3,839 |
| `legal-subject/774` | douanes et accises | 2,647 |
| `legal-subject/871` | médecine | 2,097 |
| `legal-subject/988` | magistrature | 1,861 |
| `legal-subject/686` | bétails et animaux domestiques | 1,854 |

Labels are **French only**. A `skos:prefLabel` language survey on `legal-subject/728`
returns exactly one label, `fr`.

## 2. Why the publisher's vocabulary rather than ours

Spec §3.6 states that authority is cited data, never our opinion. A subject vocabulary
Lex invented would be an opinion about what a law is about, asserted with no citable
source, and it would be the first such content in the Luxembourg corpus.

The publisher's own classification is not that. It is an assertion Legilux makes about
its own act, retrievable, attributable and reproducible from the same endpoint as every
other fact in the corpus. Storing it is the same act as storing a publication date.

This also distinguishes publisher assertions from inferred classifications. Lex has no manual
legal-alias or model-derived legal-metadata input: Legilux subjects and EUR-Lex metadata travel
from their official source into corpus bytes and are covered by normal corpus provenance.

## 3. What is stored

Per work, per subject assertion:

| field | source |
|---|---|
| `identifier`, `source_uri` | the official concept URI verbatim |
| `value`, `label` | the French `skos:prefLabel` |
| `language` | `fr` |
| `kind` | level plus the official specific scheme |

The authority URI is the identity; the label is a rendering convenience and may change
upstream without changing the identity. Both are stored so a relabelling upstream is
visible as a change rather than a silent rewrite.

The closed kinds are
`legilux_subject_level{1|2}_{theme|organisation|place|legal_resource|country}`.
Missing or ambiguous schemes, missing French labels, unknown schemes, invalid URIs, or more than
512 records for a work fail ingestion. Canonical deduplication and sorting make the bytes stable.
The work assertion is copied to each held version because the corpus record is version-shaped;
Lex does not invent a separate validity interval for it.

## 4. How it surfaces now

- Search can match the label in the weak publisher-metadata tier and returns one bounded typed
  `matched_publisher_metadata` object explaining the match.
- A server-issued chip can replay the official concept URI through the exact
  `publisher_metadata_identifier` filter on the existing search tool.
- The metadata never establishes work authority, becomes legal evidence, or claims that the term
  occurs in the legal text.

## 5. Measured held-corpus bounds

The pre-implementation official-source audit measured 1,402 held works and 4,656 versions.
Subjects covered 1,335 works: 613 distinct level-1 concepts and 1,174 distinct level-2 concepts,
with at most 57 combined assertions on one work. No held assertion lacked a French preferred
label or an unambiguous supported specific scheme. These measurements support bounded capture;
they do not justify a cross-publisher taxonomy browser.

## 6. Costs accepted

- A second authority vocabulary to mirror, version and re-verify on every ingest, with the
  same discipline the corpus applies to every other publisher assertion.
- French-only labels. An English interface must show the French subject or leave it
  unlabelled. Translating them would re-introduce exactly the invented-authority problem
  this decision avoids.
- No false EU equivalence. EUR-Lex/EuroVoc relations and Legilux subjects retain distinct kinds;
  they are not merged into a shared taxonomy.

## 7. What this does not do

It does not add a taxonomy endpoint, hierarchy browser, domain facet, assistant-authored concept
filter, or evidence source. It improves recall only in a deliberately weak discovery tier while
title and direct legal-text matches keep precedence.
