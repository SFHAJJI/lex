# Results schema `lex-results/2` (for the evaluation builder)

The Results page (`/fr/results.html`, `/en/results.html`) reads `GET /api/next/results`, which serves `app\results.json` as
it is. A file with `"schema": "lex-results/2"` is drawn by `web\app\NextResults.jsx`; a file without it is drawn by the
earlier view (`NextPages.jsx`, the fresh test of 20/11/2026). Every figure on the page is read from this file: the page
types no number. A field that is missing is left out of the page, never guessed; a key the page does not know (a slice, a
law, a type) is shown as the file writes it.

Write counts as integers, rates as nothing (the page computes no share it is not given), intervals as `[low, high]` in
percent with one decimal (Wilson 95 %, over the denominator the row names). Dates are `yyyy-mm-dd`.
Questions, expected and given answers are shown in French as written (marked `lang="fr"` on the English page).

```json
{
  "schema": "lex-results/2",
  "run": "fresh-03",                       // the run's name, as in data\runs\
  "made": "2026-11-22",                    // the day the run was scored
  "build": { "code": "f1", "data": "20261122a" },   // the build that answered: the live one, or say which
  "models": { "question": "gpt-6-luna", "read": "gpt-6.2-sol", "embedding": "text-embedding-3-large" },

  "set": {
    "name": "set-04",
    "n": 120,                              // every question asked; the denominator of every total (K-EVAL-9)
    "written_by": "a session that never saw Lex's code, from the law's text and from an insurer's questions",
    "fixed_before_run": true,
    "used_before": false,                  // true when these questions were seen before: the page says so
    "note": "optional, one sentence"
  },

  "scoring": {
    "by": "code",                          // "code" or "model" (K-EVAL-10)
    "rule": "right: the expected article is the first block and a key phrase is in its lines",
    "lenient": ["any key phrase of several counts"]   // every lenient rule, said next to the numbers
  },

  "totals": {                              // over set.n; the states of TRACE-CONTRACT.md
    "right": 0, "wrong": 0, "partial": 0, "candidates": 0, "not_held": 0, "not_found": 0, "clarify": 0, "out_of_scope": 0,
    "intervals": { "right": [0.0, 0.0], "wrong": [0.0, 0.0] }
  },

  "wrong": [                               // every wrong answer asserted, shown first (K-EVAL-4)
    { "id": "F030", "question": "…", "expected": "Art. 104, loi du 7 décembre 2015", "given": "Art. 37, loi de 1991", "what": "article | version | lines | repealed | not_held_asserted" }
  ],

  "retrieval": { "right_article_read": 0, "of": 0, "interval": [0.0, 0.0] },   // measured apart from the answer (K-EVAL-2)

  "slices": [                              // K-EVAL-1, K-UI-8: each with its count and margin
    { "slice": "insurance", "n": 0, "right": 0, "wrong": 0, "not_found": 0, "interval": [0.0, 0.0] }
  ],
  // slice keys the page names in words: insurance, other_laws, eu, dated, list, reference, definition, no_answer,
  // out_of_scope, absent, fr, de, en, lb (any other key is shown as written)

  "by_law":  [ { "law": "contrat-assurance", "n": 0, "right": 0, "wrong": 0 } ],
  "by_type": [ { "type": "value", "n": 0, "right": 0, "wrong": 0 } ],   // rule, value, list, definition, change, procedure, other

  "misses": {                              // every question not right, labelled (K-EVAL-5)
    "corpus_gap": 0, "retrieval": 0, "reading": 0,
    "items": [ { "id": "F011", "question": "…", "label": "corpus_gap | retrieval | reading", "note": "what was missing" } ]
  },

  "absent": {                              // questions whose answer is not in what Lex holds (K-EVAL-3)
    "n": 0, "said_not_held": 0, "said_not_found": 0, "asserted": 0, "other": 0
  },

  "ablation": {                            // one brick off at a time, same build, same questions (K-EVAL-6)
    "build": { "code": "f1", "data": "20261122a" },
    "rows": [ { "what": "all | no_outline | no_words | no_meaning | no_references | no_arbiter | no_routing", "n": 0, "right": 0, "wrong": 0, "interval": [0.0, 0.0] } ]
  },

  "baseline": {                            // the naive pipeline on the same questions (K-EVAL-7)
    "what": "embeddings top-k only, the same reader",
    "n": 0, "right": 0, "wrong": 0, "interval": [0.0, 0.0],
    "naive_right_lex_not": 0,              // where naive did fine and Lex did not: said, not hidden
    "lex_right_naive_not": 0,
    "items": [ { "id": "F044", "question": "…", "naive": "right", "lex": "candidates" } ]
  },

  "noise": {                               // the same questions run again (the models are not deterministic)
    "runs": 3, "right": [0, 0, 0], "wrong": [0, 0, 0]
  },

  "cost": {                                // measured: tokens counted × list price
    "usd_per_answer": { "mean": 0.0, "median": 0.0, "p90": 0.0 },
    "model_calls_per_answer": { "mean": 0.0, "max": 0 },
    "price_source": "Azure retail price list, data zone, read 2026-11-20; not yet compared with the bill"
  },

  "latency": { "median_ms": 0, "p90_ms": 0, "max_ms": 0 }
}
```

Every section is optional; the page draws the ones present, in this order: the set and how it was scored, the wrong
answers asserted, the totals, retrieval, the slices, by law and by type (folded), the misses labelled, the absent
questions, the bricks switched off, the naive baseline, the noise, cost and time.

## Added by repair-pages (22/11/2026; nothing above was changed or removed)

The page also draws these, each optional, each where a reader of the section needs it:

```json
{
  "pipeline": "combo",                     // the pipeline measured (funnel | combo | topk | naive), said beside the build

  "previous": {                            // the previous Lex (the build the public site served before), on the same questions
    "build": { "code": "t8", "data": "20261120c" }, "pipeline": "funnel",
    "n": 120, "right": 0, "wrong": 0, "not_found": 0, "interval": [0.0, 0.0], "wrong_interval": [0.0, 0.0],
    "better": 0, "worse": 0,               // questions right now and not before / right before and not now
    "items": [ { "id": "F012", "question": "…", "before": "right | wrong | partial | candidates | not_held | not_found | clarify | out_of_scope", "now": "…" } ]
  },

  "retrieval": { "walk_found": 0, "walk_of": 0, "walk_interval": [0.0, 0.0] },   // the outline walk alone found the article (K-RETR-3)

  "by_law":  [ { "law": "…", "n": 0, "right": 0, "wrong": 0, "interval": [0.0, 0.0] } ],   // "interval" optional, also on by_type

  "baseline": { "build": { "code": "t9", "data": "20261122b" } },   // `build` optional on baseline, noise and previous
  "noise":    { "build": { "code": "t9", "data": "20261122b" } },

  "latency": { "conditions": "one question at a time, 20 s apart" }  // how the times were taken, one sentence
}
```

The page says, under its heading and in words, whether the build measured (`build`) is the one answering on the site
(`GET /api/next/config` `build`); when it is not, it says so before any number. Each section that names its own build says
it too. A section without a build is the top-level build's.

## Added by rc3 (22/11/2026)

```json
{
  "build": { "code": "t10", "data": "20261122c", "engine": "01f1b08c054c474c" },   // `python app/engine_id.py --git <tag>`
  "totals": { "asserted_incomplete": 0, "error": 0 },          // drawn with the other states
  "misses": { "gate": 0, "gate_means": { "fr": "…", "en": "…" } },   // the decision cause, and what it means
  "ablation": { "rule": "… exact McNemar p < 0.05 against both release runs",
                "rows": [ { "what": "all", "run": "R1" }, { "what": "no_corpus_model", "effect": "measured | no measured effect on this set", "p_vs_R1": 0.0, "p_vs_R2": 0.0 } ] },
  "scoring": { "checked_by": { "fr": "…", "en": "…" } }
}
```

`build.engine` is the fingerprint of the files that compute an answer (`app/engine_id.py`: sha256 over server, core, funnel,
refs, question, question_topk, build_lexical, llm, dictionary.json, coverage_gaps.json; 16 hex digits). The server reports its
own in `/api/next/config` `build.engine`, computed at start from the files it runs. When the code tags differ but the data tags
and the two fingerprints are equal, the page says the figures describe the build answering (`data-results-build="same-engine"`),
and no section is said not to be the version answering. Any other difference is said as before.

## Added by fix-pages3 (22/11/2026; nothing above was changed or removed)

The page now draws several runs, and what the walk down the tables of contents adds. Two new top-level keys, both optional;
the top-level run (sealed-set-04 today) stays as it is and is drawn as one run among the others.

### `runs`: more runs on the same page (sealed-set-05: rc4 against the live rc3)

```json
{
  "runs": [
    {
      "key": "sealed-05",                  // unique, [a-z0-9-]+: the run's anchor on the page (#run-sealed-05)
      "status": "final",                   // "final": drawn whole. "pending": the page names the run, its set and its builds,
                                           //   says it is not scored yet, and draws no figure. Anything else: as "pending".
      "title": { "fr": "rc4 contre la version en ligne (rc3)", "en": "rc4 against the live version (rc3)" },   // optional, one line

      // then every key of a top-level run, in the same shape as above (schema optional here):
      "run": "sealed-05", "made": "2026-11-22",
      "build": { "code": "t12", "data": "20261122d", "engine": "0123456789abcdef" },   // the candidate measured (rc4); engine = engine_id.py
      "pipeline": "combo",
      "models": { "question": "gpt-6-luna", "read": "gpt-6.2-sol", "embedding": "text-embedding-3-large" },
      "set": { "name": "sealed-set-05", "n": 0, "written_by": { "fr": "…", "en": "…" }, "fixed_before_run": true, "used_before": false,
               "note": { "fr": "…", "en": "…" } },   // note: only what written_by and fixed_before_run / used_before do not already say
      "scoring": { … }, "totals": { … }, "wrong": [ … ], "retrieval": { … }, "slices": [ … ], "by_law": [ … ], "by_type": [ … ],
      "misses": { … }, "absent": { … }, "baseline": { … }, "noise": { … }, "cost": { … }, "latency": { … }, "ablation": { … },

      "previous": {                        // the version it is compared with, on the same questions: here the live rc3
        "name": { "fr": "rc3, la version en ligne", "en": "rc3, the live version" },   // optional: the row's name (else "the previous one")
        "build": { "code": "t11", "data": "20261122c", "engine": "01f1b08c054c474c" }, "pipeline": "combo",
        "where": "https://law.soufien.lu, POST /api/next/ask, one question at a time, 6 s apart",
        "n": 0, "right": 0, "wrong": 0, "not_found": 0, "interval": [0.0, 0.0], "wrong_interval": [0.0, 0.0],
        "better": 0, "worse": 0,           // right in this run and not in the previous / the other way round
        "p": 0.0,                          // exact two-sided McNemar on those two counts (optional)
        "items": [ { "id": "b012", "question": "…", "before": "right", "now": "partial" } ]
      },

      "decision": {                        // the fixed rule and what it gave (optional; also read on the top-level run)
        "rule": { "fr": "…", "en": "…" },  // or one string
        "result": "PROMOTE",               // PROMOTE | KEEP | any other word, shown as written
        "checks": [ { "pair": "R1~rc3", "right": [0, 0], "discordant": [0, 0], "p": 0.0, "wrong_asserted": [0, 0] } ]
      }
    }
  ]
}
```

- The page puts first a table of the runs (the top-level one and each of `runs`): the set, its size, the build measured, right and
  wrong asserted with their intervals, the decision, and whether the build measured is the one answering on the site (by code:
  the same code and data tags, or the same data tag and the same `build.engine` fingerprint as `/api/next/config`).
- Then each run in its own section, the run that describes the site's answers first, the others by `made`, newest first. A run
  that is not the version answering says so under its heading, before any number.
- `previous.build` equal to the build answering on the site is said to be the version answering ("rc3, the live version").
- Write the sealed-set-05 run as one entry of `runs`; do not change the top-level run (sealed-set-04).

### `walk_value`: what the walk down the tables of contents adds (walk-analysis, from the saved runs; no new run)

```json
{
  "walk_value": {
    "run": "sealed-04",                    // the run whose saved answers were read
    "made": "2026-11-22",
    "build": { "code": "t10", "data": "20261122c", "engine": "01f1b08c054c474c" },   // the build of those answers
    "by": "code",                          // computed by code from the saved answers and the set's gold
    "method": { "fr": "…", "en": "…" },    // one or two sentences: what was read, how "found" is decided
    "n": 260,                              // the questions read
    "of": 222,                             // the questions with a gold article (the denominator of `recall` unless a row says another)

    "recall": [                            // the gold article among what each search (or the walk) brought, measured apart
      { "search": "walk_parts", "found": 0, "of": 222, "interval": [0.0, 0.0] }
    ],
    // search keys the page names in words: walk_parts (in a part the walk opened), walk_stop (the article the walk stopped on),
    // outline (the outline column of the detectors), exact (named in the question), words, meaning, words_all, meaning_all,
    // dictionary, routing (the text was kept at the corpus level), any (any search), read (among the articles read)
    // (any other key is shown as written)

    "only": [                              // the gold article brought by this search and by no other
      { "search": "walk_parts", "found": 0, "of": 222, "right": 0 }   // right: of those, answered right by the full run
    ],

    "ablation": {                          // the walk switched off (arm no_outline) against the full run, same questions
      "with": { "run": "R1", "right": 208 }, "without": { "run": "no_outline", "right": 210 }, "n": 260,
      "discordant": [0, 0],                // right with the walk only / right without it only
      "p": 0.80,                           // exact two-sided McNemar
      "slices": [ { "slice": "insurance", "n": 0, "with": 0, "without": 0, "discordant": [0, 0], "p": 0.0 } ]   // slice keys as `slices`
    },

    "cost": {                              // what the walk costs, from the traces; shares in percent, one decimal
      "calls_per_answer": 0.0, "share_of_calls": 0.0, "ms_per_answer": 0, "share_of_time": 0.0,
      "usd_per_answer": 0.0, "share_of_cost": 0.0
    },

    "findings": [ { "fr": "…", "en": "…" } ],   // each one sentence, with figures that are in the fields above
    "verdict": { "fr": "…", "en": "…" }          // one sentence: what the walk is worth on this set
  }
}
```

- The page draws `walk_value` as its own section after the table of runs, with the build it read (said not to be the version
  answering when it is not). Every section is optional: a missing field is left out, never guessed.
- `findings` and `verdict` are the evaluation's own words; the page draws them as written, in the page's language when the file
  gives both.
