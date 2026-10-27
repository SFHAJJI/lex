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
//
// All three are one mount, so they carry one history, and it is a CHAINED one, because the captured
// answer is a genesis log and already shows that case: four builds in the log, the text of three held
// (the mounted build and two earlier generations the retention line kept, one of them for two
// reasons) and one named by the log alone. The builds are placed so the retention line
// (`V3GenerationRetention.Decide`) decides exactly what is listed: the first is its UTC day's last
// build and its month's earliest, the second September's earliest held, the third neither, and the
// mounted build is the fourth and last of its day.

import { page } from './render.mjs';
import { renderCoverage } from './coverage.mjs';
import { skinFor } from './shells.mjs';

const PUBLISHER = 'preview-synthetic';

const CORPUS = 'c0'.repeat(32);
const INDEX = 'd1'.repeat(32);
const REGISTRY = 'e2'.repeat(32);

// The scope of a mount whose build reported its text yield (`V3CorpusMount.CoverageScopeWithTextYield`), as the captured
// answer's mount does: the catalogue's versions are then counted too.
const SCOPE =
  'the mounted Luxembourg corpus and index: what this mount holds and recorded as missing, and of what the publisher '
  + "holds nothing but text_yield's count of the versions its catalogue lists, as this mount holds the catalogue's facts";

// The platform's own sentence, to the character (`V3CorpusMount.TextYieldNote`).
const TEXT_YIELD_NOTE =
  "the build's report of the text yield per class, read from the build report beside this mount and held to it when the mount opened: "
  + 'per publisher and per class of act (a Luxembourg family and state kind; an EU content class), versions counts the versions the catalogue lists '
  + "(Luxembourg: the catalogue states of the class, as the index holds the catalogue's facts, and every state of which a file is held; EU: the census's "
  + 'versions, one per original wording and one per consolidation date of each seed) and versions_with_text those with an article served here; '
  + 'versions_not_held counts each catalogue state of which this mount holds no file, by what the catalogue says of it, and is null where the index cannot '
  + 'tell (EU); files counts the publisher files this mount holds, each in exactly one of files_with_text, files_withheld (by the rights disposition, '
  + 'verbatim; pdf counts the PDF files among them), files_pdf_only (an admitted PDF from which no text is served; null where the corpus records no '
  + 'file format: EU) and files_parse_failed (an admitted file from which no article is served and that is not counted as PDF only, by the '
  + "derivation's own dispositions, verbatim, joined with +; for the EU, a file with no Formex main body, a PDF or HTML one among them, is counted here "
  + "by its own disposition, such as formex_main_body_not_eligible); articles counts the articles served; works counts distinct acts, so a "
  + "publisher's works are not the sum of its classes', while its "
  + "versions, files and articles are; annexes counts the annex outcomes the corpus recorded on the class's files: image_only those of "
  + 'annex_text_not_available, with_text those served as text (none: no annex is served as an article) and failed every other, by disposition, and is '
  + 'null where the corpus records no annex outcome (Luxembourg)';

// The yield the build reported, in the shape the captured answer carries (one Luxembourg class, every list empty and no
// annex outcome), with synthetic figures that hold to the rest of this answer as the producer holds a report to its mount:
// its articles are the 240 the index holds and its files the 12 members acquired. Twelve files with text hold eleven of
// the class's thirty versions (one version is held in two languages) and ten of its fourteen acts.
const TEXT_YIELD = Object.freeze({
  publishers: Object.freeze([
    Object.freeze({
      articles: 240,
      classes: Object.freeze([
        Object.freeze({
          annexes: null,
          articles: 240,
          family: 'loi',
          files: 12,
          files_parse_failed: Object.freeze([]),
          files_pdf_only: 0,
          files_with_text: 12,
          files_withheld: Object.freeze([]),
          kind: 'consolidation',
          versions: 30,
          versions_not_held: Object.freeze([]),
          versions_with_text: 11,
          works: 14,
          works_with_text: 10,
        }),
      ]),
      files: 12,
      files_with_text: 12,
      publisher: 'lu-legilux',
      versions: 30,
      versions_with_text: 11,
      works: 14,
      works_with_text: 10,
    }),
  ]),
  schema: 'lex-v3-text-yield/1',
});

function textYield() {
  return structuredClone(TEXT_YIELD);
}

const COUNTS_NOTE =
  'counts are of rows the index holds; totals.articles and languages[].articles count the '
  + 'articles the index holds, and the corpus can have recorded more articles than the index '
  + 'holds: members.article_outcomes counts what it recorded; a missing publisher date is counted '
  + 'as missing and never dropped; articles_with_searchable_text is what the capability cells '
  + 'measure: each article with searchable text, at its own publisher date where it carries one, '
  + 'else at the date of the state that holds it, and an article with neither is not counted; so '
  + 'it and articles_without_publisher_date are not addends; when a language is requested, '
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
  'served_operations are the operations this mount answers from its data and not_served_operations are registered and '
  + 'served from no data; every registered operation has a route over REST and MCP, and a request for an unserved one answers '
  + 'inside a normal envelope with the typed refusal its row of not_served_data gives (reason: retrieval_mode_unavailable, '
  + 'naming the requested mode and the modes this server serves), never a transport failure; not_served_data names, for each, '
  + 'the data that would serve it, none of which the ingest produces';

const SERVED = Object.freeze([
  'answer_drift', 'article_history', 'as_observed', 'as_of', 'ask', 'browse', 'changes_in_period',
  'citation', 'cited_by', 'classification', 'coverage', 'diff', 'dossier', 'events',
  'evidence_bundle', 'in_force_on', 'manifestation', 'provenance', 'relations', 'resolve', 'search',
  'status_on', 'timeline', 'verify',
]);

const NOT_SERVED = Object.freeze([
  'concepts', 'knowable_on', 'transposition',
]);

// The platform's own sentences, to the character (`V3CorpusMount.NotServedDataNeeded`).
const NOT_SERVED_DATA = Object.freeze([
  Object.freeze({
    data_needed: 'the concept data attached to EU works: EuroVoc descriptors, EU directory codes and '
      + 'subject matters as the Publications Office records them; the EU index holds none of them',
    operation: 'concepts',
    reason: 'retrieval_mode_unavailable',
    requested_mode: 'r5_concepts',
  }),
  Object.freeze({
    data_needed: 'each held state\'s publication date beside its observation time (observed_from), so a '
      + 'date is answered with what a reader could have known on it, never with the publisher\'s '
      + "valid-from date; the observation times need each Luxembourg body's capture time in the "
      + "corpus, which no build records yet: a build's time bounds observation only from above",
    operation: 'knowable_on',
    reason: 'retrieval_mode_unavailable',
    requested_mode: 'r6_knowable_on',
  }),
  Object.freeze({
    data_needed: 'the transposition links: Legilux\'s transposes and draftTransposes assertions and the '
      + 'Publications Office\'s national implementing measures for Luxembourg, each kept as its '
      + 'publisher asserts it and never merged; neither is acquired',
    operation: 'transposition',
    reason: 'retrieval_mode_unavailable',
    requested_mode: 'r4_transposition',
  }),
]);

// The platform's own sentence, to the character (`V3CorpusMount.HistoryNote`).
const HISTORY_NOTE =
  'the builds the mounted Luxembourg log records (its snapshots), and those whose text this mount holds: the mounted build, and each '
  + 'earlier generation the retention line keeps beside it (S7-A09: referenced generations indefinitely, the last build of each UTC day '
  + "for 90 days, each UTC month's earliest indefinitely), held to the log when the mount opened; a snapshot without text is named by the "
  + "log alone, and as_observed answers its states without text; every time here is a build's, an upper bound on observation, never an "
  + 'observation time';

const HISTORY = Object.freeze({
  history_begins: '2026-08-31T23:00:00Z',
  log_records_builds: true,
  note: HISTORY_NOTE,
  retention_policy: Object.freeze({
    evaluated_at: '2026-09-01T18:00:00Z',
    id: 'lex-v3-generation-retention/1',
    nightly_days: 90,
  }),
  snapshots_in_log: 4,
  snapshots_with_text: Object.freeze([
    Object.freeze({
      built_at: '2026-08-31T23:00:00Z', observation: 1, retained_as: Object.freeze(['nightly', 'monthly_keeper']), snapshot_id: 'a3'.repeat(32),
    }),
    Object.freeze({
      built_at: '2026-09-01T06:00:00Z', observation: 2, retained_as: Object.freeze(['monthly_keeper']), snapshot_id: 'b4'.repeat(32),
    }),
    Object.freeze({
      built_at: '2026-09-01T18:00:00Z', observation: 4, retained_as: Object.freeze(['mounted']), snapshot_id: INDEX,
    }),
  ]),
  snapshots_without_text: 1,
});

function history() {
  return {
    ...HISTORY,
    retention_policy: { ...HISTORY.retention_policy },
    snapshots_with_text: HISTORY.snapshots_with_text.map((snapshot) => ({
      ...snapshot, retained_as: [...snapshot.retained_as],
    })),
  };
}

const NOT_HELD = Object.freeze([
  Object.freeze({
    item: 'publisher_universe',
    // The row of a mount whose build reported its text yield (`V3CorpusMount.CoveragePublisherUniverseWithTextYield`).
    reason: 'how many acts the publisher holds outside the families whose catalogue facts this mount holds: text_yield counts '
      + 'the versions and acts the catalogue lists in those families, and the EU versions the census found for the seeds '
      + 'acquired, and nothing here counts any other',
  }),
  Object.freeze({
    item: 'never_consolidated_acts',
    reason: 'the count of as-published acts never consolidated is a corpus-level statement this '
      + 'mount does not carry',
  }),
  Object.freeze({
    item: 'first_sighting_and_observation_times',
    reason: 'no observation time is held, so nothing here says when anything was first seen; '
      + 'events serves the event log, whose first_sighting events say only that a state is first '
      + 'present in that log',
  }),
  Object.freeze({
    item: 'build_time_and_currency',
    // Index 9's row (S7c, the row S4 left): a build's time is its acquisition's, and a derivation's is the log's derived_at.
    reason: "history gives each build's time, when its acquiring build read once its acquisition had ended: an upper bound "
      + 'on when its corpus was observed and never an observation time, which an index derived again from custody keeps; '
      + "events gives log.derived_at, when the mounted index was derived from custody, null when its derivations are the "
      + "acquiring build's own; nothing here says how current these counts are against the publisher; no build time of the "
      + 'corpus file itself is held; the corpus and index digests name exactly which artifacts are mounted',
  }),
  Object.freeze({
    item: 'legal_status',
    reason: "no status, repeal or commencement fact is counted here; status_on serves the publisher's force assertions per work, verbatim",
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
// date and hold no searchable text, so nothing about it can be searched: the index measured no
// capability for it, the flag is false and the count is zero. (A missing date alone no longer means
// that: an undated article is measured at its state's date, 2026-10-13.)
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

/**
 * What the mount declares beside its counts (R78, R12; R82's languages are null, as on a mount with no EU index, which the
 * platform's own census is): two Luxembourg classes it does not hold, each with its disposition, and when Lex's own
 * observations of Legilux begin, on the first build the history's log records.
 */
function declaredScope() {
  return {
    europe_languages: null,
    luxembourg_classes_not_held: [
      { class: 'communal_regulations', disposition: 'out_of_scope' },
      { class: 'RCSF', disposition: 'admitted_awaiting_a_run' },
    ],
    // The history's first build, 2026-08-31T23:00:00Z, is 1 September in Luxembourg.
    observations_begin: [{ basis: 'first_build_in_log', date: '2026-09-01', publisher: 'lu-legilux' }],
    rule: DECLARED_SCOPE_RULE,
  };
}

/** The platform's rule for what the mount declares, as `V3CorpusMount.DeclaredScopeRule` words it. */
const DECLARED_SCOPE_RULE =
  'luxembourg_classes_not_held declares, once per class, each Luxembourg class this mount does not hold, with its disposition; '
  + 'observations_begin gives, per publisher, when Lex’s own observations held here begin';

function answer({ requestedLanguage, languages, capabilityCells }) {
  return {
    capability_cells: capabilityCells,
    counts_note: COUNTS_NOTE,
    declared_scope: declaredScope(),
    history: history(),
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
      not_served_data: NOT_SERVED_DATA.map((row) => ({ ...row })),
      not_served_operations: [...NOT_SERVED],
      note: OPERATIONS_NOTE,
      registered: SERVED.length + NOT_SERVED.length,
      served_operations: [...SERVED],
    },
    requested_language: requestedLanguage,
    scope: SCOPE,
    text_yield: textYield(),
    text_yield_note: TEXT_YIELD_NOTE,
    totals: { ...TOTALS },
  };
}

const WHOLE_MOUNT = {
  heading: 'The whole mount',
  note:
    'Two language rows that disagree on purpose. One holds searchable text across four measured '
    + 'periods and knows when its states begin and end; the other holds forty articles, none with '
    + 'a publisher date or searchable text, so nothing about it can be searched and its two date cells '
    + 'say "not stated by the platform" rather than sitting blank. The works column sums past the '
    + 'total and is meant to: a work published in two languages is one work in two rows. Its log '
    + 'records four builds and the mount holds the text of three: its own and two earlier ones the '
    + 'retention line kept, each with the reasons it was kept.',
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
      + '      <p>Nothing on it says how current the counts are. Its build times, in the history section, '
      + 'say when each build ran, an upper bound on observation, and none of them dates the counts; what '
      + 'names the artifacts these counts came from is a pair of digests. The calendar dates in the '
      + 'tables are the publisher’s facts about the law and are a different kind of thing.</p>\n'
      + '      <p>Every value on this page is synthetic and none of it is law.</p>\n'
      + PREVIEW_ANSWERS.map((preview) => (
        `      <section class="coverage-case"><h2>${preview.heading}</h2>`
        + `<p class="coverage-case-note">${preview.note}</p>`
        + renderCoverage(preview.answer)
        + '</section>\n'
      )).join(''),
  });
}
