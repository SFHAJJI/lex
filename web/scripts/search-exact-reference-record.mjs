// The live API's answer to search for "Loi du 27 juillet 1997 sur le contrat d'assurance, article 38, 1er juin 2012"
// (https://law.soufien.lu, 2026-11-15): an exact reference that opens article 38 in the version of 16/04/2011. A record for
// the V4 search page's tests (`live-home.test.mjs`), kept here because the tree admits test files only as `*.test.mjs`.
export const SEARCH_EXACT_REFERENCE_2026_11_15 = Object.freeze({
  "context": {
    "commits": {
      "index_builder_source_commit": "bfb839839ea7d49cec79e8525275968d1000be8e",
      "serving_runtime_source_commit": "38c1a47bac9af8ff055568523eb228b0b9f7066a"
    },
    "freshness": {
      "channels": [
        {
          "age_days": 2,
          "lag_days": null,
          "lag_observations": 0,
          "newest_completed_observation": "2026-11-12T15:22:05Z",
          "publisher": "lu-legilux",
          "stale": false
        },
        {
          "age_days": 3,
          "lag_days": null,
          "lag_observations": 0,
          "newest_completed_observation": "2026-11-11T19:24:48Z",
          "publisher": "eu-eurlex",
          "stale": false
        }
      ],
      "index_built_at": "2026-11-12T15:22:06Z",
      "last_checked_against_publisher": null,
      "maximum_data_age_days": 30,
      "observed_at": "2026-11-15T13:51:35.2725340Z",
      "stale": false,
      "upstream_health": "stale"
    },
    "jurisdiction": "lu",
    "provisional": false,
    "publisher": "lu-legilux",
    "snapshot": {
      "snapshot_id": "corpus-b2c3c1ba55bdda9a",
      "snapshot_sha256": "b2c3c1ba55bdda9aab9039675d97e835765252e1f5703fbf713d083203d47bf7"
    },
    "status": "success",
    "timeline_semantics": "publisher_applicability"
  },
  "object_type": "envelope",
  "operation_id": "search",
  "refusal": null,
  "registry_schema": "lex-v3-operation-registry/2",
  "registry_sha256": "627879b6718d746b944758378b9ea476919311a72857bcba0cb72e202fb70059",
  "request_ref": "http_8e6cf447ea920990d319ecaab5358f5e7fdf2c218de08198d24c1f22eaac2307",
  "result": {
    "object_type": "quote",
    "schema": "lex-v3-search-result/1",
    "value": {
      "corpus_sha256": "b2c3c1ba55bdda9aab9039675d97e835765252e1f5703fbf713d083203d47bf7",
      "hits": [],
      "index_sha256": "e3a11e8e50f7538c580b1c3e18a980d7a9b19d0ee6fb6906e77af82bf20bdcc8",
      "lanes_run": false,
      "publisher": "lu-legilux",
      "reference": {
        "kind": "citation",
        "opens": {
          "address": "/lu-legilux/loi-1997-07-27-n10/2011-04-16--f3222d2f1fca421b94dbc4bafd934eac08ed60c88b128e297bb1a4878aa3f4fa/fra/art_38",
          "address_kind": "permalink",
          "article_id": "art_38",
          "language": "fra",
          "page": "reading",
          "publisher": "lu-legilux",
          "state_date": "2011-04-16",
          "work": "loi-1997-07-27-n10"
        },
        "outcome": "opens",
        "phase": "exact_reference",
        "reason": null,
        "recognised": {
          "act": {
            "as_typed": "Loi du 27 juillet 1997 sur le contrat d'assurance",
            "form": "type_and_date",
            "identifier": "Loi du 27 juillet 1997 sur le contrat d'assurance",
            "title": "Version consolidée applicable au 01/01/2016 : Loi du 27 juillet 1997 sur le contrat d'assurance.",
            "title_language": "fra",
            "work": "loi-1997-07-27-n10"
          },
          "article": {
            "as_typed": "38",
            "key": "38",
            "matched_by": "number",
            "number": "38",
            "part": null
          },
          "article_rule": "An article number, as a citation writes it and as an index holds it, is read to one key, and a citation opens the article whose key is its key: never a near one. Case, spacing and the code letter's dot are not part of a number (L. 121-6, L.121-6 and L 121-6 are L.121-6); 1er, premier and 1 are 1; a suffix is part of a number (42, 42bis, 42ter and 42-1 are four numbers); an arabic number is never a roman one (3 is not III). A grouped article (a range such as 141 à 144, or two numbers joined) is opened by a citation of a number it groups only when no article of the version holds that number itself. An id this table reads as no number of the act (a quoted article of an amended act) is never opened by a citation.",
          "date": {
            "as_typed": "1er juin 2012",
            "date": "2012-06-01",
            "source": "citation"
          },
          "language": "fra",
          "text": "Loi du 27 juillet 1997 sur le contrat d'assurance, article 38, 1er juin 2012"
        },
        "rule": "before any word is searched, the query is read as an exact reference: an ELI, a CELEX (consolidated forms included), a Memorial or Journal officiel A citation, a historical identifier of the publisher's catalogue, an address of this site for law, an act by its type and date or an EU act by its conventional name, or a citation of an article of an act and of a date; a reference the indexes hold opens its act, at the article and date it names (the date defaults to the search's date, else to today in Luxembourg), or its dossier when it names neither, and no word is searched; an identifier, an EU act's name or a type and date that names no act the indexes or the publisher's catalogue hold is refused identifier_unknown, with no list; one act the publisher's catalogue holds and the indexes hold no version of is refused text_not_available with its record (R77); a day, an issue or a name that names several acts is refused ambiguous_identifier with every one, held or not, and none is chosen; a title the index does not hold, and words after a type and date that name none of that day's acts, open nothing on their own: a title is searched as words, and the day's acts are listed; a date in a date's shape that names no calendar day opens nothing; a query that is no reference is searched as words"
      },
      "requested_after": null,
      "requested_date": null,
      "requested_identifier": null,
      "requested_language": "fra",
      "requested_mode": "strict",
      "requested_query": "Loi du 27 juillet 1997 sur le contrat d'assurance, article 38, 1er juin 2012"
    }
  },
  "schema": "lex-v3-envelope/1",
  "verdict": "answer",
  "version": "v3"
});
