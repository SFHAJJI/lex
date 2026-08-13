# Luxembourg subject facets from the publisher's own taxonomy

Status: **planned** (D79). Nothing in this document is implemented.

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

This also distinguishes it from the EU side. `config/eu-work-enrichment.json` carries
reviewed aliases produced with model assistance and gated on confidence, agreement across
repeat runs and evidence anchors. That machinery exists because those values are inferred.
Subject assertions are not inferred, so they must **not** travel through the reviewed-alias
path, whose provenance fields (model, prompt digest, confidence, agreement ratio) would
have to be fabricated to fit.

## 3. What would be stored

Per work, per subject assertion:

| field | source |
|---|---|
| `authority_uri` | the `legal-subject/{id}` URI verbatim |
| `label` | `skos:prefLabel`, French |
| `level` | 1 or 2, from which predicate carried it |

The authority URI is the identity; the label is a rendering convenience and may change
upstream without changing the identity. Both are stored so a relabelling upstream is
visible as a change rather than a silent rewrite.

Subjects attach to the **work**, not to a dated version. The publisher asserts them on
`jolux:Act`, and nothing in the dataset says a subject applies only from a given date, so
inventing a validity interval for them would be manufacturing temporal precision the
source does not have.

## 4. How it would surface

- A `subject` filter on `search`, `in_force_on` and `changes_in_period`, alongside the
  existing `document_type`, `hierarchy`, `act_form`, `binding_status` and `domain`.
- Facet counts on the catalogue page, grouped by level 1 with level 2 nested.
- The subject shown on a work page as publisher-asserted metadata, labelled as such.

## 5. Open measurements

These are unknown and must be measured before implementation, not assumed:

1. **Coverage against our scope.** The counts above are across the whole Legilux dataset.
   How many of the 1,399 works Lex actually holds carry a level-1 subject is not known.
   A facet present on a small minority of works is worse than no facet.
2. **Cardinality.** How many distinct level-1 and level-2 subjects appear within our
   scope, and whether the distribution is usable as a filter or is a long tail.
3. **Multiplicity.** Whether an act may carry several level-1 subjects, and if so what
   the maximum is, which decides whether the facet is single or multi valued.
4. **Level-2 to level-1 relationship.** Whether level 2 is a strict child of level 1 in
   the authority scheme, or an independent axis that merely happens to be numbered.

## 6. Costs accepted

- A second authority vocabulary to mirror, version and re-verify on every ingest, with the
  same discipline the corpus applies to every other publisher assertion.
- French-only labels. An English interface must show the French subject or leave it
  unlabelled. Translating them would re-introduce exactly the invented-authority problem
  this decision avoids.
- No EU equivalent. EUR-Lex does not expose this vocabulary, so the two publishers' subject
  facets are not interchangeable and must never be merged into a single cross-publisher
  list. A shared facet control implies a shared vocabulary, and there is not one.

## 7. What this does not do

It does not improve retrieval ranking, and it should not be justified as if it did. It is
a navigation and scoping affordance. A subject filter narrows a result set the user is
already looking at; it does not change which provisions match a query.
