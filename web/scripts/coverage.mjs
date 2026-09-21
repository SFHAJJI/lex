// Coverage: the page whose job is to say what is missing.
//
// Every other screen answers a question. This one exists to be checked against, so its failure
// mode is not a wrong answer but a comfortable one: a count presented as current, a breakdown that
// reads as complete because nothing said it was not, two numbers in one row that cannot both be
// true. It is written against what the V3 platform's `coverage` operation really sends, captured in
// `schemas/v3-platform/answer-samples.json` by driving the real handler, and the tests render that
// captured answer rather than a shape anyone imagined.
//
// THIS PAGE USED TO PRINT A BUILD DATE ON EVERY TABLE AND A RETENTION SENTENCE UNDER THE COUNTS.
// It was written against a V2 payload carrying `envelope.freshness.built_at`, and it stamped
// `Counts as of index build <instant>.` into the body and into both table captions, and printed
// `Observation history begins August 2026; replay depth grows from here.` The V3 answer holds
// neither fact and says so in its own words, in two of the five rows of its fixed `not_held` list:
//
//   build_time_and_currency -- "no build time of the corpus or index is held, so nothing here says
//   how current these counts are; the corpus and index digests name exactly which artifacts are
//   mounted"
//
//   first_sighting_and_observation_times -- "no observation time or first-sighting event is held,
//   so nothing here says when anything was first seen"
//
// So there is no date on this page and no retention sentence. What stands in their place is what
// the platform does hold: `mounted.corpus_sha256` and `mounted.index_sha256`, which name exactly
// which artifacts these counts were taken from. The envelope's `context.freshness.observed_at` is
// the nearest thing to a date anywhere near this answer, and it is when the answer was produced
// rather than when the counts were measured; this reader is handed the result value and never the
// envelope, and `refuseRetiredShapes` refuses an `envelope` member by name, so the substitution
// cannot be made by accident. Recorded in advance on PR #711, comment 5752822407.
//
// The second rule is the one the old page attached to the wrong noun. It reconciled each facet
// TABLE as a partition or as an overlap. The V3 language table is both, per COLUMN, and the SQL
// behind it says which is which (`LuxembourgIndexBuilder.ResolveCoverage`):
//
//   states   -- COUNT(*) FROM states GROUP BY language, against COUNT(*) FROM states. Every state
//               row carries one language, so the rows partition, and when nothing was narrowed
//               away they sum to the total exactly.
//   articles -- COUNT(*) FROM articles GROUP BY language, against COUNT(*) FROM articles. A
//               partition too, except that a language present in `articles` and absent from
//               `states` gets no row at all, so its articles are in the total and in no row. The
//               sum is a bound here and not an identity.
//   works    -- COUNT(DISTINCT work_key) FROM states GROUP BY language, against COUNT(DISTINCT
//               work_key) FROM states. A work published in two languages is one work in two rows,
//               so these rows overlap and are never summed; only the per-row bound holds.
//
// One rule for the table would either invent the identity for `works` -- which is how the V2 page
// would have refused both live coverage pages, measured, 1,406 language works against 1,402 held
// -- or drop it for `states`.
//
// The third rule is a sum this page must NOT do, and the platform says so itself in `counts_note`:
// "articles_with_searchable_text is counted where the article carries a publisher date, which is
// what the capability cells measure, and so it and articles_without_publisher_date are not
// addends". An article can carry a date and hold no searchable text. The V2 page REQUIRED exactly
// this sum of its own two text columns and would have refused every answer whose columns were
// honest.
//
// The fourth is narrowing, which on this answer is narrower than it looks. `counts_note` says that
// when a language is requested "only languages and capability_cells are narrowed to it, and every
// other member, totals included, is the whole mount's". A narrowed answer therefore puts one
// language's rows beside the whole mount's totals, and a reader who takes those totals for that
// language's reads the mount as smaller than it is. The page says so where the totals are rather
// than in a footnote, and renders `counts_note` verbatim as well.

import { NOT_STATED, escapeHtml } from './render.mjs';
import { isCalendarDate } from './temporal.mjs';

export { NOT_STATED };

const DIGEST = /^[0-9a-f]{64}$/;

/**
 * The members the retired page required, refused by name.
 *
 * Eleven top-level members were required by the V2 renderer: `envelope`, `publisher_name`, `works`,
 * `versions`, `text`, `valid_from_earliest`, `valid_from_latest`, `known_gaps`, `document_types`,
 * `document_types_total` and `languages`. Ten of them are absent from the V3 answer and are listed
 * here. The eleventh, `languages`, IS sent under the same name with a different row shape -- V2
 * rows are `{code, works, versions}` and V3 rows are keyed `language` and counted in `states` --
 * so it is caught by the row reader rather than by name, and `readLanguageRow` says so where it
 * refuses.
 *
 * Without this, feeding the V2 shape to this page produces a page missing its date rather than an
 * error, and a reader cannot tell a platform that holds no build time from a page that forgot to
 * print one.
 */
function refuseRetiredShapes(answer) {
  const retired = [
    'envelope',
    'publisher_name',
    'works',
    'versions',
    'text',
    'valid_from_earliest',
    'valid_from_latest',
    'known_gaps',
    'document_types',
    'document_types_total',
  ];
  for (const member of retired) {
    if (Object.hasOwn(answer ?? {}, member)) {
      throw new Error(
        `this coverage answer carries ${member}, which belongs to the payload this page was `
          + 'written against before V3. That payload carried a build instant and this one records '
          + 'that no build time is held, so rendering the old shape here would put a date on counts '
          + 'the platform refuses to date',
      );
    }
  }
}

function requireOwn(object, key, where) {
  if (!Object.hasOwn(object ?? {}, key)) {
    throw new Error(
      `${where} does not carry ${key}; an absent member and a member with nothing in it are `
        + 'different facts, and only one of them can be reported',
    );
  }
  return object[key];
}

function requireText(value, where) {
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw new Error(`${where} is not a value this page can print`);
  }
  return value;
}

function requireDigest(value, where) {
  if (typeof value !== 'string' || !DIGEST.test(value)) {
    throw new Error(`${where} is not a SHA-256 digest: ${JSON.stringify(value)}`);
  }
  return value;
}

/**
 * A count, which is a whole number that is not negative.
 *
 * This page has no defaults and nothing here falls back to zero, because a figure the renderer
 * supplies is a figure nobody measured.
 */
function requireCount(value, where) {
  if (!Number.isInteger(value) || value < 0) {
    throw new Error(
      `${where} is ${JSON.stringify(value)} rather than a count; this page has no defaults, `
        + 'because a figure the renderer supplies is a figure nobody measured',
    );
  }
  return value;
}

function requireList(value, where) {
  if (!Array.isArray(value)) {
    throw new Error(`${where} is a list, even an empty one`);
  }
  return value;
}

/** A calendar date the platform may or may not hold. Null is a fact; anything else is refused. */
function optionalDate(value, where) {
  if (value === null) return null;
  if (!isCalendarDate(value)) {
    throw new Error(`${where} is not a calendar date: ${JSON.stringify(value)}`);
  }
  return value;
}

function requireAtMost(value, bound, where, why) {
  if (value > bound) {
    throw new Error(`${where} is ${value} against ${bound}; ${why}`);
  }
  return value;
}

function requireDistinct(keys, where) {
  const seen = new Set();
  for (const key of keys) {
    if (seen.has(key)) {
      throw new Error(
        `${where} lists ${JSON.stringify(key)} twice; one key is one row, so a reader cannot tell `
          + 'which of the two is the figure for it, and any total that reconciles means only that '
          + 'the duplicate was counted consistently',
      );
    }
    seen.add(key);
  }
  return keys;
}

/**
 * A breakdown row counts at least one of the thing it breaks down.
 *
 * Both breakdowns under `members` are SQL `GROUP BY`s, and a group exists because at least one row
 * produced it, so neither can honestly count nought. A row counting nobody is a token or an outcome
 * nothing recorded, and on the page whose job is to be checked against, a row that exists and
 * accounts for nothing is worse than a missing row: it reads as a category this corpus knows about.
 */
function requireCountedByAtLeastOne(rows, key, where) {
  for (const [index, row] of rows.entries()) {
    if (row.members === 0) {
      throw new Error(
        `${where}[${index}] counts no members for ${JSON.stringify(row[key])}; these rows are a `
          + 'grouping of the members, and a group exists because a member is in it, so a row '
          + 'accounting for nobody is a category nothing recorded',
      );
    }
  }
  return rows;
}

function readTotals(totals) {
  const where = 'totals';
  const read = {
    members: requireCount(requireOwn(totals, 'members', where), `${where}.members`),
    works: requireCount(requireOwn(totals, 'works', where), `${where}.works`),
    states: requireCount(requireOwn(totals, 'states', where), `${where}.states`),
    articles: requireCount(requireOwn(totals, 'articles', where), `${where}.articles`),
  };
  // `works` is COUNT(DISTINCT work_key) over the same rows `states` is COUNT(*) over, so a mount
  // cannot hold more works than the states they were counted from.
  requireAtMost(
    read.works, read.states, 'totals.works',
    'works are the distinct works of the held states, so there cannot be more of them than there '
      + 'are states',
  );
  return read;
}

/**
 * One language's row.
 *
 * `searchable_text_held` and `articles_with_searchable_text` are checked against each other in both
 * directions, which is airtight rather than merely plausible: the flag is true exactly when the
 * capability manifest holds a search/articles/searchable_text cell for that language, the count is
 * the sum of those cells' populations, and `V3IndexCapabilityCell` refuses a population of zero or
 * less because "an advertised capability must have measured support". So the flag is true if and
 * only if the count is above zero, and a row saying no searchable text is held beside a positive
 * count of articles with searchable text is a contradiction rather than a nuance.
 */
function readLanguageRow(row, index, totals) {
  const where = `languages[${index}]`;
  if (!Object.hasOwn(row ?? {}, 'language') && Object.hasOwn(row ?? {}, 'code')) {
    throw new Error(
      `${where} is keyed by code rather than by language; that is the row shape of the payload this `
        + 'page was written against before V3, whose columns counted versions rather than states',
    );
  }

  const read = {
    language: requireText(row?.language, `${where}.language`),
    works: requireCount(requireOwn(row, 'works', where), `${where}.works`),
    states: requireCount(requireOwn(row, 'states', where), `${where}.states`),
    articles: requireCount(requireOwn(row, 'articles', where), `${where}.articles`),
    articles_with_searchable_text: requireCount(
      requireOwn(row, 'articles_with_searchable_text', where),
      `${where}.articles_with_searchable_text`),
    articles_without_publisher_date: requireCount(
      requireOwn(row, 'articles_without_publisher_date', where),
      `${where}.articles_without_publisher_date`),
    first_state_date: optionalDate(
      requireOwn(row, 'first_state_date', where), `${where}.first_state_date`),
    last_state_date: optionalDate(
      requireOwn(row, 'last_state_date', where), `${where}.last_state_date`),
    searchable_text_held: requireOwn(row, 'searchable_text_held', where),
  };

  if (typeof read.searchable_text_held !== 'boolean') {
    throw new Error(
      `${where}.searchable_text_held is whether a search can be asked in this language, which is `
        + 'true or false and never absent',
    );
  }
  if (read.searchable_text_held !== read.articles_with_searchable_text > 0) {
    throw new Error(
      `${where} says searchable text ${read.searchable_text_held ? 'is' : 'is not'} held and counts `
        + `${read.articles_with_searchable_text} articles with searchable text; the flag is the `
        + 'existence of a measured capability cell and the count is the sum of those cells, and a '
        + 'cell with no measured support is refused where it is built, so these two cannot disagree',
    );
  }

  // The per-row bound, which holds whether or not the answer was narrowed: narrowing drops rows and
  // leaves the totals whole, so a subset's row is bounded by the whole mount's total just as the
  // full set's row is.
  requireAtMost(read.works, totals.works, `${where}.works`,
    'a language cannot hold more works than the mount holds');
  requireAtMost(read.states, totals.states, `${where}.states`,
    'a language cannot hold more states than the mount holds');
  requireAtMost(read.articles, totals.articles, `${where}.articles`,
    'a language cannot hold more articles than the mount holds');

  // Each of the two article columns is bounded by the articles of its own row, and neither is added
  // to the other or to anything else: `counts_note` says they are not addends, because an article
  // can carry a publisher date and hold no searchable text.
  requireAtMost(
    read.articles_without_publisher_date, read.articles,
    `${where}.articles_without_publisher_date`,
    'the articles missing a publisher date are counted among this language’s articles',
  );

  if (read.first_state_date !== null && read.last_state_date !== null
    && read.first_state_date > read.last_state_date) {
    throw new Error(
      `${where} reports states running from ${read.first_state_date} to ${read.last_state_date}, `
        + 'which ends before it begins; these are the ends of one interval rather than two '
        + 'independent dates, and an interval that runs backwards is not a smaller range but a '
        + 'wrong one',
    );
  }
  // MIN and MAX over the same column are both null or neither is. One of each says a row was
  // assembled from two different measurements.
  if ((read.first_state_date === null) !== (read.last_state_date === null)) {
    throw new Error(
      `${where} holds one end of its date range and not the other; both are taken over the same `
        + 'column of the same rows, so a language with a first state has a last one',
    );
  }
  return read;
}

function readLanguages(value, { totals, languagesHeld, requestedLanguage }) {
  const rows = requireList(value, 'languages').map(
    (row, index) => readLanguageRow(row, index, totals));
  requireDistinct(rows.map((row) => row.language), 'the language breakdown');

  const held = new Set(languagesHeld);
  for (const row of rows) {
    if (!held.has(row.language)) {
      throw new Error(
        `the language breakdown counts ${JSON.stringify(row.language)}, which is not among the `
          + 'languages this mount records holding; languages_held is taken from these same rows '
          + 'before any narrowing, so a row outside it is a row from somewhere else',
      );
    }
  }

  if (requestedLanguage === null) {
    // Nothing was narrowed away, so the rows and the held list are the same set. Both directions
    // are needed: the loop above gives one, and the sizes give the other.
    if (rows.length !== languagesHeld.length) {
      throw new Error(
        `no language was asked for and ${rows.length} of the ${languagesHeld.length} languages this `
          + 'mount holds have a row; an unnarrowed answer breaks down every language it holds, and '
          + 'a table that simply stops reads as a complete one',
      );
    }
    // The partition: every state row carries exactly one language, so with nothing narrowed away
    // the rows account for every state exactly once.
    const states = rows.reduce((sum, row) => sum + row.states, 0);
    if (states !== totals.states) {
      throw new Error(
        `the language breakdown accounts for ${states} states against a total of ${totals.states}, `
          + 'and no language was asked for; every state carries exactly one language, so a complete '
          + 'breakdown that does not add up to its own headline means one of the two was measured '
          + 'against something else',
      );
    }
  } else {
    for (const row of rows) {
      if (row.language !== requestedLanguage) {
        throw new Error(
          `${JSON.stringify(requestedLanguage)} was asked for and the breakdown counts `
            + `${JSON.stringify(row.language)}; a narrowed answer holds the rows of the language it `
            + 'was narrowed to and no others',
        );
      }
    }
  }

  // A bound rather than an identity, narrowed or not. An article whose language no state is held in
  // is counted in the total and has no row to be counted in, so the rows can fall short of the
  // total honestly; they can never exceed it.
  const articles = rows.reduce((sum, row) => sum + row.articles, 0);
  requireAtMost(articles, totals.articles, 'the articles the language breakdown accounts for',
    'the rows are taken from the articles the total counts, so together they cannot exceed it');

  // `works` is never summed. A work published in two languages is one work in two rows, so the sum
  // is expected to exceed the total and means nothing; only the per-row bound above holds.
  return rows;
}

/**
 * What the corpus recorded about the documents it went to get.
 *
 * The two breakdowns here reconcile differently and the SQL says which is which. `by_outcome` is
 * `SELECT outcome,COUNT(*) FROM members GROUP BY outcome` against `SELECT COUNT(*) FROM members`,
 * so every member is in exactly one row and the rows sum to the total exactly. `gaps` is
 * `SELECT j.value,COUNT(DISTINCT m.object_ref_sha256) ... json_each(m.gaps_json)`, so a member
 * carrying two gap tokens is counted in two rows and the rows must never be summed; each is
 * bounded by the number of members carrying any gap at all.
 */
function readMembers(value, totals) {
  const where = 'members';
  const withGaps = requireCount(requireOwn(value, 'with_gaps', where), `${where}.with_gaps`);
  requireAtMost(withGaps, totals.members, `${where}.with_gaps`,
    'the members carrying a gap are counted among the members');

  const byOutcome = requireList(requireOwn(value, 'by_outcome', where), `${where}.by_outcome`)
    .map((row, index) => ({
      outcome: requireText(row?.outcome, `${where}.by_outcome[${index}].outcome`),
      members: requireCount(
        requireOwn(row, 'members', `${where}.by_outcome[${index}]`),
        `${where}.by_outcome[${index}].members`),
    }));
  requireDistinct(byOutcome.map((row) => row.outcome), 'the outcome breakdown');
  requireCountedByAtLeastOne(byOutcome, 'outcome', `${where}.by_outcome`);
  const counted = byOutcome.reduce((sum, row) => sum + row.members, 0);
  if (counted !== totals.members) {
    throw new Error(
      `the outcome breakdown accounts for ${counted} members against a total of ${totals.members}; `
        + 'every member has exactly one outcome and these rows are that grouping, so a breakdown '
        + 'that does not add up to its own headline means one of the two was measured against '
        + 'something else',
    );
  }

  const gaps = requireList(requireOwn(value, 'gaps', where), `${where}.gaps`)
    .map((row, index) => ({
      gap: requireText(row?.gap, `${where}.gaps[${index}].gap`),
      members: requireCount(
        requireOwn(row, 'members', `${where}.gaps[${index}]`),
        `${where}.gaps[${index}].members`),
    }));
  requireDistinct(gaps.map((row) => row.gap), 'the gap breakdown');
  requireCountedByAtLeastOne(gaps, 'gap', `${where}.gaps`);
  for (const [index, row] of gaps.entries()) {
    requireAtMost(row.members, withGaps, `${where}.gaps[${index}].members`,
      'a gap token cannot be recorded by more members than the number of members recording any gap');
  }
  // The bound above is also what refuses a `with_gaps` of zero beside any gap row, so there is no
  // separate check for it: a token is counted only where a member recorded it, and a row counting
  // one or more members against nought members with gaps fails the bound in its own terms. The
  // converse -- members carrying gaps and no token counted -- is NOT refused, because a member
  // whose gap list is written `[ ]` is counted in `with_gaps` and yields no row through
  // `json_each`. That answer is rendered as the fact it is, with a sentence saying this page cannot
  // say which gap.

  return {
    withGaps,
    byOutcome,
    gaps,
    gapsNote: requireText(requireOwn(value, 'gaps_note', where), `${where}.gaps_note`),
  };
}

/**
 * What this mount answers, and what is registered with no route on it.
 *
 * The producer builds `not_served_operations` as `registered.Except(served)`, so the two lists add
 * up to `registered` exactly when the served operations are a subset of the registered ones. When
 * they do not add up, a route is served for an operation the reviewed registry does not register,
 * which is the one thing this arithmetic can detect and is worth detecting.
 */
function readOperations(value) {
  const where = 'operations';
  const registered = requireCount(requireOwn(value, 'registered', where), `${where}.registered`);
  const served = requireList(
    requireOwn(value, 'served_operations', where), `${where}.served_operations`)
    .map((operation, index) => requireText(operation, `${where}.served_operations[${index}]`));
  const notServed = requireList(
    requireOwn(value, 'not_served_operations', where), `${where}.not_served_operations`)
    .map((operation, index) => requireText(operation, `${where}.not_served_operations[${index}]`));
  requireDistinct(served, 'the served operations');
  requireDistinct(notServed, 'the operations with no route');

  // This answer exists, so the operation that produced it is one this mount answers. A served list
  // that leaves it out is describing some other mount.
  if (!served.includes('coverage')) {
    throw new Error(
      'the served operations do not include coverage, and this is a coverage answer; a list of what '
        + 'a mount answers that omits the operation which just answered is a list about something '
        + 'else',
    );
  }
  const overlap = served.filter((operation) => notServed.includes(operation));
  if (overlap.length > 0) {
    throw new Error(
      `${overlap.join(', ')} is listed both as served and as having no route; the two lists are what `
        + 'this mount answers and what it does not, and an operation cannot be in both',
    );
  }
  if (served.length + notServed.length !== registered) {
    throw new Error(
      `${served.length} served and ${notServed.length} unrouted operations are listed against `
        + `${registered} registered; the unrouted list is the registered operations minus the served `
        + 'ones, so these add up, and at the producer the one way they do not is a route served for '
        + 'an operation the reviewed registry does not register',
    );
  }

  return {
    registered,
    served,
    notServed,
    note: requireText(requireOwn(value, 'note', where), `${where}.note`),
  };
}

/**
 * What the index measured it can be asked.
 *
 * A cell's period is inclusive and its population is above zero, both refused at construction by
 * `V3IndexCapabilityCell`; the manifest itself refuses duplicate cells and overlapping periods for
 * one set of dimensions. Checked again here because this reader is handed JSON rather than the
 * object, and a value that survived construction can still be altered between there and here.
 */
function readCells(value) {
  const cells = requireList(value, 'capability_cells').map((measured, index) => {
    const where = `capability_cells[${index}]`;
    const read = {
      operation: requireText(measured?.operation, `${where}.operation`),
      column: requireText(measured?.column, `${where}.column`),
      field: requireText(measured?.field, `${where}.field`),
      language: requireText(measured?.language, `${where}.language`),
      period_from: requireOwn(measured, 'period_from', where),
      period_to: requireOwn(measured, 'period_to', where),
      population: requireCount(requireOwn(measured, 'population', where), `${where}.population`),
    };
    for (const end of ['period_from', 'period_to']) {
      if (!isCalendarDate(read[end])) {
        throw new Error(`${where}.${end} is not a calendar date: ${JSON.stringify(read[end])}`);
      }
    }
    if (read.period_from > read.period_to) {
      throw new Error(
        `${where} covers ${read.period_from} to ${read.period_to}, which ends before it begins; the `
          + 'period is inclusive and its ends are not two independent dates',
      );
    }
    if (read.population === 0) {
      throw new Error(
        `${where} advertises a capability with a population of zero; a measured capability has `
          + 'measured support, and a cell with none is refused where cells are built',
      );
    }
    return read;
  });
  requireDistinct(
    cells.map((measured) => [measured.operation, measured.column, measured.field,
      measured.language, measured.period_from, measured.period_to].join('\u0000')),
    'the measured capabilities',
  );
  return cells;
}

/**
 * Validates one `coverage_report` and returns what the renderers lay out.
 *
 * Every rule lives here and is applied once, so the React port can share them rather than
 * reimplement them beside a copy that drifts. The two coverage renderers carried exactly that
 * duplication until now, held together by a parity test that fed both the same inputs; the defect
 * it was guarding against is the one the dossier's two renderers had, and the repair is the one
 * `readProvenance` made -- one validator, two layouts.
 */
export function readCoverage(answer) {
  refuseRetiredShapes(answer);

  const where = 'this coverage answer';
  const requestedLanguage = requireOwn(answer, 'requested_language', where);
  if (requestedLanguage !== null
    && (typeof requestedLanguage !== 'string' || requestedLanguage.trim().length === 0)) {
    throw new Error('requested_language is the language asked for, or null when none was asked');
  }

  const languagesHeld = requireList(requireOwn(answer, 'languages_held', where), 'languages_held')
    .map((language, index) => requireText(language, `languages_held[${index}]`));
  requireDistinct(languagesHeld, 'the languages this mount holds');

  const mountedValue = requireOwn(answer, 'mounted', where);
  const mounted = {
    publisher: requireText(mountedValue?.publisher, 'mounted.publisher'),
    corpus_sha256: requireDigest(mountedValue?.corpus_sha256, 'mounted.corpus_sha256'),
    index_sha256: requireDigest(mountedValue?.index_sha256, 'mounted.index_sha256'),
    registry_sha256: requireDigest(mountedValue?.registry_sha256, 'mounted.registry_sha256'),
  };

  const totals = readTotals(requireOwn(answer, 'totals', where));
  const languages = readLanguages(requireOwn(answer, 'languages', where), {
    totals, languagesHeld, requestedLanguage,
  });

  // An empty breakdown is a fact and gets a sentence rather than an empty table -- but only where it
  // can be true. With nothing narrowed away the rows and `languages_held` are the same set, so no
  // rows beside a non-empty held list is a contradiction. `readLanguages` has already refused it on
  // the sizes; this says it in its own terms and does not depend on that.
  if (languages.length === 0 && requestedLanguage === null && languagesHeld.length > 0) {
    throw new Error(
      `no language was asked for, ${languagesHeld.length} languages are recorded as held and none `
        + 'has a row; a mount that holds languages breaks them down',
    );
  }

  // Every row with its reason. This is the page's whole account of what it cannot tell a reader,
  // and a row without a reason is worse than no row: it names a gap and explains nothing.
  const notHeld = requireList(requireOwn(answer, 'not_held', where), 'not_held');
  if (notHeld.length === 0) {
    throw new Error(
      'not_held is this answer’s account of what it does not hold; an empty list would read as '
        + 'an answer that holds everything, on the page whose job is to say what is missing',
    );
  }

  return {
    scope: requireText(requireOwn(answer, 'scope', where), 'scope'),
    countsNote: requireText(requireOwn(answer, 'counts_note', where), 'counts_note'),
    requestedLanguage,
    languagesHeld,
    mounted,
    totals,
    languages,
    members: readMembers(requireOwn(answer, 'members', where), totals),
    operations: readOperations(requireOwn(answer, 'operations', where)),
    capabilityCells: readCells(requireOwn(answer, 'capability_cells', where)),
    notHeld: notHeld.map((row, index) => ({
      item: requireText(row?.item, `not_held[${index}].item`),
      reason: requireText(row?.reason, `not_held[${index}].reason`),
    })),
  };
}

/**
 * What a language-narrowed coverage answer is, said where the totals are.
 *
 * `counts_note` is rendered verbatim as well and says the same thing; this is the page's own
 * sentence, placed beside the numbers it is about, because a note read after the totals is read
 * after the totals are believed.
 */
export function narrowedNote(language) {
  return (
    `This answer was narrowed to ${language} when it was requested. Only the language rows and the `
    + 'measured capabilities are that language’s. The totals here, the members below and '
    + 'everything else on this page are the whole mount’s.'
  );
}

/** The mount measured a capability for an operation it does not serve. Said, never hidden. */
export function unservedCapabilityNote(operations) {
  return (
    `The index measured a capability for ${operations.join(', ')}, which this mount does not serve. `
    + 'What the index measured and what the mount answers are two different facts, and here they '
    + 'disagree.'
  );
}

/** Which measured capabilities name an operation this mount does not serve. Often none. */
export function unservedCapabilities(view) {
  const served = new Set(view.operations.served);
  return [...new Set(view.capabilityCells
    .map((measured) => measured.operation)
    .filter((operation) => !served.has(operation)))];
}

const code = (value) => `<code>${escapeHtml(String(value))}</code>`;

/** A value the platform holds, or the sentence saying it does not. Never a blank cell. */
function cell(value, render) {
  return value === null
    ? `<span class="coverage-not-stated">${escapeHtml(NOT_STATED)}</span>`
    : render(value);
}

function row(label, value) {
  return `<tr><th scope="row">${escapeHtml(label)}</th><td>${value}</td></tr>`;
}

/**
 * A table in its own scroll box.
 *
 * The box is keyboard focusable whether or not it asks to be, because a scrollable region is, so it
 * carries a role and an accessible name rather than becoming a tab stop that announces nothing. The
 * caption says what the table counts. It does not say when, because nothing on this answer says
 * when.
 */
function table({ caption, head, rows }) {
  return (
    '<div class="coverage-scroll" role="region" tabindex="0" '
    + `aria-label="${escapeHtml(caption)}, scrollable">`
    + `<table class="coverage-table"><caption>${escapeHtml(caption)}</caption><thead><tr>`
    + head.map((heading) => `<th scope="col">${escapeHtml(heading)}</th>`).join('')
    + `</tr></thead><tbody>${rows}</tbody></table></div>`
  );
}

/**
 * Whether a search can be asked in this language, as the platform's own flag.
 *
 * It looks derived, because `readCoverage` proves it equals whether the count beside it is above
 * zero. It is shown anyway, and that is the point: this is the page a reader checks the others
 * against, so a reader who wants to check that equality should be able to see both sides of it
 * rather than take this page's word that it held.
 */
export const HELD = Object.freeze({ true: 'yes', false: 'no' });

function languageRows(languages) {
  return languages.map((language) => (
    '<tr>'
    + `<td>${escapeHtml(language.language)}</td>`
    + `<td>${language.works}</td>`
    + `<td>${language.states}</td>`
    + `<td>${language.articles}</td>`
    + `<td>${HELD[language.searchable_text_held]}</td>`
    + `<td>${language.articles_with_searchable_text}</td>`
    + `<td>${language.articles_without_publisher_date}</td>`
    + `<td>${cell(language.first_state_date, escapeHtml)}</td>`
    + `<td>${cell(language.last_state_date, escapeHtml)}</td>`
    + '</tr>'
  )).join('');
}

/** The page. Every rule is in readCoverage; this decides only how the result looks. */
export function renderCoverage(answer) {
  const view = readCoverage(answer);
  const unserved = unservedCapabilities(view);
  return (
    '<section class="coverage">'
    + '<section class="coverage-block"><h2>What this page is about</h2>'
    + `<p class="coverage-scope">${escapeHtml(view.scope)}</p>`
    + '<table class="coverage-facts"><tbody>'
    + row('publisher', code(view.mounted.publisher))
    + row('corpus', code(view.mounted.corpus_sha256))
    + row('index', code(view.mounted.index_sha256))
    + row('operation registry', code(view.mounted.registry_sha256))
    + '</tbody></table>'
    + '<p class="coverage-note">These counts were taken from the corpus and index named above. '
    + 'There is no date on this page because no build time is held; the digests say exactly which '
    + 'artifacts were counted, which a date does not.</p>'
    + '</section>'
    + '<section class="coverage-block"><h2>How these counts are counted</h2>'
    + `<p class="coverage-note">${escapeHtml(view.countsNote)}</p></section>`
    + '<section class="coverage-block"><h2>What this mount holds</h2>'
    + '<table class="coverage-facts"><tbody>'
    + row('works', escapeHtml(String(view.totals.works)))
    + row('states', escapeHtml(String(view.totals.states)))
    + row('articles', escapeHtml(String(view.totals.articles)))
    + row('members', escapeHtml(String(view.totals.members)))
    + '</tbody></table>'
    + (view.requestedLanguage === null
      ? ''
      : `<p class="coverage-note">${escapeHtml(narrowedNote(view.requestedLanguage))}</p>`)
    + `<p class="coverage-held">Languages held: ${view.languagesHeld.map(code).join(' ')}</p>`
    + (view.languages.length === 0
      ? '<p class="coverage-note">No language has a row here, so nothing below breaks these totals '
        + 'down.</p>'
      : table({
        caption: 'Held works, states and articles by language',
        head: ['language', 'works', 'states', 'articles', 'searchable text held',
          'articles with searchable text', 'articles with no publisher date', 'first state',
          'last state'],
        rows: languageRows(view.languages),
      }))
    + '</section>'
    + '<section class="coverage-block"><h2>What the corpus recorded for its members</h2>'
    + table({
      caption: 'Members by the outcome the corpus recorded',
      head: ['outcome', 'members'],
      rows: view.members.byOutcome.map((outcome) => (
        `<tr><td>${code(outcome.outcome)}</td><td>${outcome.members}</td></tr>`)).join(''),
    })
    + `<p class="coverage-held">${view.members.withGaps} of ${view.totals.members} members `
    + 'recorded a gap.</p>'
    + (view.members.gaps.length === 0
      ? '<p class="coverage-note">No gap token is counted here, so where a member above recorded a '
        + 'gap this page cannot say which.</p>'
      : table({
        caption: 'Gap tokens the corpus recorded, counted by member',
        head: ['gap', 'members'],
        rows: view.members.gaps.map((gap) => (
          `<tr><td>${code(gap.gap)}</td><td>${gap.members}</td></tr>`)).join(''),
      }))
    + `<p class="coverage-note">${escapeHtml(view.members.gapsNote)}</p>`
    + '</section>'
    + '<section class="coverage-block"><h2>What can be asked of this mount</h2>'
    + `<p class="coverage-held">${view.operations.served.length} of `
    + `${view.operations.registered} registered operations are answered here.</p>`
    + '<table class="coverage-facts"><tbody>'
    + row('answered', view.operations.served.map(code).join(' '))
    + row('registered, with no route on this mount', view.operations.notServed.length === 0
      ? 'none'
      : view.operations.notServed.map(code).join(' '))
    + '</tbody></table>'
    + `<p class="coverage-note">${escapeHtml(view.operations.note)}</p></section>`
    + '<section class="coverage-block"><h2>What this mount measured it can answer</h2>'
    + (view.capabilityCells.length === 0
      ? '<p class="coverage-note">No capability was measured, so nothing here says what this mount '
        + 'can be asked of any period.</p>'
      : table({
        caption: 'Measured capabilities, by operation, column, field, language and period',
        head: ['operation', 'column', 'field', 'language', 'from', 'to', 'population'],
        rows: view.capabilityCells.map((measured) => (
          '<tr>'
          + `<td>${code(measured.operation)}</td>`
          + `<td>${code(measured.column)}</td>`
          + `<td>${code(measured.field)}</td>`
          + `<td>${code(measured.language)}</td>`
          + `<td>${escapeHtml(measured.period_from)}</td>`
          + `<td>${escapeHtml(measured.period_to)}</td>`
          + `<td>${measured.population}</td>`
          + '</tr>')).join(''),
      }))
    + (unserved.length === 0
      ? ''
      : `<p class="coverage-note">${escapeHtml(unservedCapabilityNote(unserved))}</p>`)
    + '</section>'
    + '<section class="coverage-block"><h2>What this mount does not hold</h2>'
    + `<ul class="coverage-not-held">${view.notHeld.map((held) =>
      `<li>${code(held.item)}: ${escapeHtml(held.reason)}</li>`).join('')}</ul>`
    + '</section>'
    + '</section>'
  );
}
