// Coverage, in the three shapes where the page says more than the numbers do.
//
// Every answer here is a `coverage_report` in the shape the V3 platform really sends. The SHAPE is
// held against the captured answer in `schemas/v3-platform/answer-samples.json` by
// `test/coverage.test.mjs`, so this file cannot drift into teaching a form no producer emits --
// which is exactly what the refusal catalogue did until a guard was written for it. The VALUES are
// synthetic on purpose and none of them is law: the banner says so.
//
// The platform's own sentences -- the scope, the counts note, the gaps note, the operations note
// and the five `not_held` rows -- are reproduced verbatim from the captured answer rather than
// paraphrased, for the same reason the provenance preview keeps the corpus's outcome and rights
// tokens: a sentence is vocabulary, not a value, and a preview teaching one nothing emits is the
// defect this bridge exists to prevent.
//
// The three shapes:
//
//   * THE WHOLE MOUNT, nothing narrowed. Two language rows that disagree with each other on
//     purpose: one holding searchable text, one holding none at all because every one of its
//     articles lacks a publisher date. A preview whose every row was the first kind would be a
//     comfortable preview of the page whose job is to be uncomfortable.
//   * NARROWED TO A LANGUAGE THE MOUNT HOLDS. One language's rows and cells beside the whole
//     mount's totals, which is what the platform does and is the easiest thing on this answer to
//     misread. The note sits with the totals rather than under them.
//   * NARROWED TO A LANGUAGE THAT MEASURES NOTHING. One row and no measured capability, because
//     narrowing dropped the other language's cells. This case replaced a preview narrowed to a
//     language the mount did NOT hold, which the producer refuses outright with
//     `language_not_available` before any answer is built: a shape nothing emits, taught by the
//     file whose header promises not to teach one, and asserted by a test. It is also the case
//     that caught the page saying "No capability was measured" about a mount that had measured
//     one.

import { page } from './render.mjs';
import { renderCoverage } from './coverage.mjs';
import { skinFor } from './shells.mjs';

const PUBLISHER = 'preview-synthetic';

const CORPUS = 'c0'.repeat(32);
const INDEX = 'd1'.repeat(32);
const REGISTRY = 'e2'.repeat(32);

const SCOPE =
  'the mounted Luxembourg corpus and index: what this mount holds and recorded as missing, and '
  + 'nothing about what the publisher holds';

const COUNTS_NOTE =
  'counts are of rows the index holds; totals.articles and languages[].articles count the '
  + 'articles the index holds, and the corpus can have recorded more articles than the index '
  + 'holds: members.article_outcomes counts what it recorded; a missing publisher date is counted '
  + 'as missing and never dropped; articles_with_searchable_text is counted where the article '
  + 'carries a publisher date, which is what the capability cells measure, and so it and '
  + 'articles_without_publisher_date are not addends; when a language is requested, '
  + 'requested_language echoes it and only languages and capability_cells are narrowed to it, and '
  + "every other member, totals included, is the whole mount's";

const GAPS_NOTE = 'the gap tokens the corpus recorded per member, verbatim, counted by member';

const ARTICLE_OUTCOMES_NOTE =
  "the corpus's own record of what its legal-content stage did with the articles of its "
  + "acquired Luxembourg documents, counted by the corpus's disposition token and given verbatim, "
  + "and this answer does not define the tokens; an outcome is one article's, except "
  + "akn_upstream_not_inventoried, which is one document's because none of its articles was "
  + 'listed; the articles the index holds, which totals.articles and languages[].articles count, '
  + 'are exactly the akn_admitted and akn_marker_only_evidence outcomes, and an outcome under any '
  + 'other token is not held here; which article an outcome belongs to is not held; the outcomes '
  + 'of members that are not acquired are not counted';

// The corpus's own disposition tokens, in ordinal order as the platform sends them, and not
// invented here (the tests read the enum that declares them). The articles the index holds are
// exactly the first two, which is why 236 and 4 are the 240 articles in the totals above; the third
// is outcomes the corpus recorded and the index does not hold.
const ARTICLE_OUTCOMES = Object.freeze([
  Object.freeze({ disposition: 'akn_admitted', outcomes: 236 }),
  Object.freeze({ disposition: 'akn_marker_only_evidence', outcomes: 4 }),
  Object.freeze({ disposition: 'akn_unsupported_content_shape', outcomes: 9 }),
]);

const OPERATIONS_NOTE =
  'served_operations are the routes this mount answers and not_served_operations are registered '
  + 'with no route on it; this states a fact about the mount and says nothing about what a request '
  + 'for an unserved operation returns';

const SERVED = Object.freeze([
  'article_history', 'as_of', 'changes_in_period', 'citation', 'cited_by', 'coverage', 'diff',
  'dossier', 'in_force_on', 'provenance', 'resolve', 'search', 'timeline',
]);

const NOT_SERVED = Object.freeze([
  'answer_drift', 'as_observed', 'ask', 'browse', 'classification', 'concepts', 'events',
  'evidence_bundle', 'knowable_on', 'manifestation', 'relations', 'status_on', 'transposition',
  'verify',
]);

const NOT_HELD = Object.freeze([
  Object.freeze({
    item: 'publisher_universe',
    reason: 'how many acts the publisher holds, or how many of them this mount lacks: the mount '
      + 'records only what was admitted',
  }),
  Object.freeze({
    item: 'never_consolidated_acts',
    reason: 'the count of as-published acts never consolidated is a corpus-level statement this '
      + 'mount does not carry',
  }),
  Object.freeze({
    item: 'first_sighting_and_observation_times',
    reason: 'no observation time or first-sighting event is held, so nothing here says when '
      + 'anything was first seen',
  }),
  Object.freeze({
    item: 'build_time_and_currency',
    reason: 'no build time of the corpus or index is held, so nothing here says how current these '
      + 'counts are; the corpus and index digests name exactly which artifacts are mounted',
  }),
  Object.freeze({
    item: 'legal_status',
    reason: 'no status, repeal or commencement fact is held; nothing here speaks of legal status',
  }),
]);

const TOTALS = Object.freeze({ members: 15, works: 12, states: 18, articles: 240 });

const LANGUAGES_HELD = Object.freeze(['fra', 'deu']);

// The four cells' populations sum to fra's `articles_with_searchable_text`, because that is what
// the producer does: the count is the sum of the search/articles/searchable_text cells for that
// language. A preview where the two disagreed would teach a shape the handler cannot produce.
//
// Each period is a SINGLE DAY, for the same reason. `MeasureCapabilities` groups articles by
// (language, wording date) and passes that one date as both ends of the cell, so a multi-day period
// is a shape the builder cannot emit. This preview carried four multi-year ranges until that was
// read off the producer.
const FRA_CELLS = Object.freeze([
  Object.freeze({ period_from: '1972-03-04', period_to: '1972-03-04', population: 5 }),
  Object.freeze({ period_from: '2000-01-01', period_to: '2000-01-01', population: 15 }),
  Object.freeze({ period_from: '2015-06-30', period_to: '2015-06-30', population: 120 }),
  Object.freeze({ period_from: '2029-11-30', period_to: '2029-11-30', population: 40 }),
]);

const FRA = Object.freeze({
  language: 'fra',
  works: 12,
  states: 15,
  articles: 200,
  articles_with_searchable_text: 180,
  articles_without_publisher_date: 12,
  first_state_date: '1972-03-04',
  last_state_date: '2029-11-30',
  searchable_text_held: true,
});

// The uncomfortable row, and the one this preview exists for. Its articles all lack a publisher
// date, so nothing about it can be searched: the index measured no capability for it, the flag is
// false and the count is zero.
//
// Its state dates are VALUES and not nulls. `states.applicability_date` is `NOT NULL` and a
// language row exists only because `states GROUP BY language` produced a group, so MIN and MAX over
// it are always dates. This row carried nulls until that was read off the DDL, and the page grew a
// "not stated by the platform" cell for a shape no producer can send. The column that can be
// missing a publisher date is `articles.applicability_date`, which is counted by
// `articles_without_publisher_date` and never dated.
const DEU = Object.freeze({
  language: 'deu',
  works: 3,
  states: 3,
  articles: 40,
  articles_with_searchable_text: 0,
  articles_without_publisher_date: 40,
  first_state_date: '1998-01-01',
  last_state_date: '2016-04-12',
  searchable_text_held: false,
});

function cells(language, periods) {
  return periods.map((period) => ({
    operation: 'search',
    column: 'articles',
    field: 'searchable_text',
    language,
    period_from: period.period_from,
    period_to: period.period_to,
    population: period.population,
  }));
}

function answer({ requestedLanguage, languages, capabilityCells }) {
  return {
    capability_cells: capabilityCells,
    counts_note: COUNTS_NOTE,
    languages,
    languages_held: [...LANGUAGES_HELD],
    members: {
      article_outcomes: ARTICLE_OUTCOMES.map((row) => ({ ...row })),
      article_outcomes_note: ARTICLE_OUTCOMES_NOTE,
      by_outcome: [
        { members: 12, outcome: 'acquired' },
        { members: 3, outcome: 'unavailable' },
      ],
      gaps: [{ gap: 'point', members: 4 }],
      gaps_note: GAPS_NOTE,
      with_gaps: 4,
    },
    mounted: {
      corpus_sha256: CORPUS,
      index_sha256: INDEX,
      publisher: PUBLISHER,
      registry_sha256: REGISTRY,
    },
    not_held: NOT_HELD.map((row) => ({ ...row })),
    operations: {
      not_served_operations: [...NOT_SERVED],
      note: OPERATIONS_NOTE,
      registered: SERVED.length + NOT_SERVED.length,
      served_operations: [...SERVED],
    },
    requested_language: requestedLanguage,
    scope: SCOPE,
    totals: { ...TOTALS },
  };
}

const WHOLE_MOUNT = {
  heading: 'The whole mount',
  note:
    'Two language rows that disagree on purpose. One holds searchable text across four measured '
    + 'periods and knows when its states begin and end; the other holds forty articles of which '
    + 'none carries a publisher date, so nothing about it can be searched and its two date cells '
    + 'say "not stated by the platform" rather than sitting blank. The works column sums past the '
    + 'total and is meant to: a work published in two languages is one work in two rows.',
  answer: answer({
    requestedLanguage: null,
    languages: [{ ...FRA }, { ...DEU }],
    capabilityCells: cells('fra', FRA_CELLS),
  }),
};

const NARROWED_TO_HELD = {
  heading: 'Narrowed to a language this mount holds',
  note:
    'One language’s rows and measured capabilities beside the whole mount’s totals, which is what '
    + 'the platform sends and is the easiest thing on this answer to misread. The totals above the '
    + 'table are not this language’s, and the sentence saying so sits with them rather than under '
    + 'them.',
  answer: answer({
    requestedLanguage: 'fra',
    languages: [{ ...FRA }],
    capabilityCells: cells('fra', FRA_CELLS),
  }),
};

const NARROWED_TO_UNMEASURED = {
  heading: 'Narrowed to a language that measures nothing',
  note:
    'One row and no measured capability. What the page must not say here is that the mount '
    + 'measured nothing: it measured four capabilities and every one of them is the other '
    + 'language’s, which narrowing dropped. The sentence is about the language asked for and '
    + 'says nothing about the mount, and it said the opposite until an answer in exactly this '
    + 'shape was built and read.',
  answer: answer({
    requestedLanguage: 'deu',
    languages: [{ ...DEU }],
    capabilityCells: [],
  }),
};

export const PREVIEW_ANSWERS = Object.freeze([
  WHOLE_MOUNT, NARROWED_TO_HELD, NARROWED_TO_UNMEASURED,
]);

/** The coverage preview, in the Gateway shell, because its reader is checking the service. */
export function renderCoveragePreview({ locale = 'en' } = {}) {
  return page({
    state: 'coverage',
    title: 'Coverage',
    locale,
    shell: 'dev',
    density: skinFor('dev').density,
    main:
      '      <p class="eyebrow">Gateway</p>\n'
      + '      <h1>Coverage</h1>\n'
      + '      <p>This is the page whose job is to say what is missing, so its failure mode is not '
      + 'a wrong answer but a comfortable one: a count presented as current, a breakdown that reads '
      + 'as complete because nothing said it was not, two numbers in one row that cannot both be '
      + 'true.</p>\n'
      + '      <p>Nothing on it says when the counting happened. This mount holds no build time and '
      + 'records that it does not, so what names the artifacts these counts came from is a pair of '
      + 'digests rather than an instant. The calendar dates in the tables are the publisher’s facts '
      + 'about the law and are a different kind of thing.</p>\n'
      + '      <p>Every value on this page is synthetic and none of it is law.</p>\n'
      + PREVIEW_ANSWERS.map((preview) => (
        `      <section class="coverage-case"><h2>${preview.heading}</h2>`
        + `<p class="coverage-case-note">${preview.note}</p>`
        + renderCoverage(preview.answer)
        + '</section>\n'
      )).join(''),
  });
}
