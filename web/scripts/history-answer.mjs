// The V3 Luxembourg provision history, read.
//
// The provision history screen's renderer (`provision-history.mjs`) was written before V3 against a
// shape the platform does not send. This file reads the `article_history` answer (object type
// `provision_history`, publisher `lu-legilux`), held to the one captured by driving the real handler
// (`schemas/v3-platform/answer-samples.json`), and turns it into one view a live screen can render.
// It renders nothing itself.
//
// The answer is a lineage: one row per held state that carries the publisher's article id, and the
// states that do not carry it listed as absent, so a lineage that begins after the work does is never
// read as the whole history. Its counts are derived from its rows, so the reader recomputes each one
// rather than trusting it, and throws on any answer where they disagree:
//  - rows and absent states together are the states in scope, each once, in date order, each pinned
//    by a permalink that names its work, date and digest, in the language asked when one was asked;
//  - every article in a row carries the anchor asked for, and its validity flag is exactly "its own
//    date is stated and differs from the state's";
//  - a row's wording changed exactly when its articles' wording digests differ from the previous row
//    of its language; the runs per language are its first row and every change; the distinct
//    wordings are its distinct digests; the history begins at the first row;
//  - a row's next date is the next later state of its language in scope, whether or not that state
//    carries the anchor.
// Nothing is derived here that the answer does not state: no end date, no "in force", no diff text.

import { isCalendarDate } from './temporal.mjs';

const DIGEST = /^[0-9a-f]{64}$/;
const STATE_PERMALINK = /^\/lu-legilux\/([^/]+)\/(\d{4}-\d{2}-\d{2})--([0-9a-f]{64})$/;

function requireOwn(object, key, where) {
  if (object === null || typeof object !== 'object' || !Object.hasOwn(object, key)) {
    throw new Error(`${where} does not carry ${key}`);
  }
  return object[key];
}

function requireText(value, where) {
  if (typeof value !== 'string' || value.trim().length === 0) {
    throw new Error(`${where} is not a value this page can print`);
  }
  return value;
}

function requireTextOrNull(value, where) {
  return value === null ? null : requireText(value, where);
}

function requireDigest(value, where) {
  if (typeof value !== 'string' || !DIGEST.test(value)) {
    throw new Error(`${where} is not a SHA-256 digest: ${JSON.stringify(value)}`);
  }
  return value;
}

function requireCount(value, where) {
  if (!Number.isInteger(value) || value < 0) {
    throw new Error(`${where} is ${JSON.stringify(value)} rather than a count`);
  }
  return value;
}

function requireList(value, where) {
  if (!Array.isArray(value)) {
    throw new Error(`${where} is a list, even an empty one`);
  }
  return value;
}

function requireDate(value, where) {
  if (!isCalendarDate(value)) {
    throw new Error(`${where} is not a calendar date: ${JSON.stringify(value)}`);
  }
  return value;
}

function requireBoolean(value, where) {
  if (typeof value !== 'boolean') {
    throw new Error(`${where} is true or false, not ${JSON.stringify(value)}`);
  }
  return value;
}

/** A state named by its language, date, digest and permalink, the permalink checked against the other three. */
function readStateRef(state, where, workKey) {
  const language = requireText(requireOwn(state, 'language', where), `${where} language`);
  const applicabilityDate = requireDate(requireOwn(state, 'applicability_date', where), `${where} applicability_date`);
  const stateSha256 = requireDigest(requireOwn(state, 'state_sha256', where), `${where} state_sha256`);
  const permalink = requireText(requireOwn(state, 'permalink', where), `${where} permalink`);
  if (permalink !== `/lu-legilux/${workKey}/${applicabilityDate}--${stateSha256}` || !STATE_PERMALINK.test(permalink)) {
    throw new Error(`${where}'s permalink ${permalink} does not pin its work, date and digest`);
  }
  return { language, applicabilityDate, stateSha256, permalink };
}

/** The per-language counts the answer states, as a map, each language once and in the languages' order. */
function readCounts(value, where) {
  const counts = new Map();
  let previous = null;
  for (const [index, row] of requireList(value, where).entries()) {
    const language = requireText(requireOwn(row, 'language', `${where}[${index}]`), `${where}[${index}].language`);
    if (counts.has(language) || (previous !== null && language < previous)) {
      throw new Error(`${where} lists ${language} out of the languages' order or twice`);
    }
    counts.set(language, requireCount(requireOwn(row, 'count', `${where}[${index}]`), `${where}[${index}].count`));
    previous = language;
  }
  return counts;
}

/**
 * Reads one V3 Luxembourg `article_history` answer into the view a live screen renders.
 *
 * @param {object} answer the `result.value` of an article_history envelope
 * @returns {object} a frozen view; throws on any answer the rules above do not allow
 */
export function readArticleHistory(answer) {
  const where = 'this provision history';
  const publisher = requireOwn(answer, 'publisher', where);
  if (publisher !== 'lu-legilux') {
    throw new Error(`this provision history is ${JSON.stringify(publisher)}'s; this reader reads the Luxembourg (lu-legilux) history only`);
  }
  const workKey = requireText(requireOwn(answer, 'work_key', where), 'work_key');
  const anchor = requireText(requireOwn(answer, 'requested_anchor', where), 'requested_anchor');
  const language = requireTextOrNull(requireOwn(answer, 'requested_language', where), 'requested_language');
  const availableLanguages = Object.freeze(requireList(requireOwn(answer, 'available_languages', where), 'available_languages')
    .map((item, index) => requireText(item, `available_languages[${index}]`)));
  if (language !== null && !availableLanguages.includes(language)) {
    throw new Error(`the language asked for, ${language}, is not one the work is held in`);
  }

  const rows = requireList(requireOwn(answer, 'states', where), 'states').map((row, index) => {
    const at = `row ${index + 1}`;
    const state = readStateRef(row, at, workKey);
    const coordinate = requireText(requireOwn(row, 'stable_coordinate', at), `${at} stable_coordinate`);
    if (coordinate !== `/lu-legilux/${workKey}/${state.applicabilityDate}`) {
      throw new Error(`${at}'s stable coordinate ${coordinate} does not name its work and date`);
    }
    const next = requireOwn(row, 'next_applicability_date', at);
    if (next !== null) requireDate(next, `${at} next_applicability_date`);
    const articles = requireList(requireOwn(row, 'articles', at), `${at} articles`).map((article, articleIndex) => {
      const on = `${at} article ${articleIndex + 1}`;
      const publisherId = requireText(requireOwn(article, 'publisher_id', on), `${on} publisher_id`);
      if (publisherId !== anchor) {
        throw new Error(`${on} is ${publisherId}; every article in the lineage carries the anchor asked for, ${anchor}`);
      }
      const validFrom = requireOwn(article, 'article_valid_from', on);
      if (validFrom !== null) requireDate(validFrom, `${on} article_valid_from`);
      const validityConflict = requireBoolean(requireOwn(article, 'validity_conflict', on), `${on} validity_conflict`);
      if (validityConflict !== (validFrom !== null && validFrom !== state.applicabilityDate)) {
        throw new Error(`${on} says validity_conflict is ${validityConflict} for its date ${validFrom} in a state of ${state.applicabilityDate}`);
      }
      return Object.freeze({
        articleIdentitySha256: requireDigest(requireOwn(article, 'article_identity_sha256', on), `${on} article_identity_sha256`),
        publisherWid: requireTextOrNull(requireOwn(article, 'publisher_wid', on), `${on} publisher_wid`),
        validFrom,
        validityConflict,
        wordingSha256: requireDigest(requireOwn(article, 'wording_sha256', on), `${on} wording_sha256`),
      });
    });
    if (articles.length === 0) {
      throw new Error(`${at} carries no article; a state that does not carry the anchor is listed as absent`);
    }
    return {
      ...state,
      nextApplicabilityDate: next,
      stableCoordinate: coordinate,
      articles: Object.freeze(articles),
      wordingChanged: requireBoolean(requireOwn(row, 'wording_changed', at), `${at} wording_changed`),
    };
  });
  if (rows.length === 0) {
    throw new Error('a lineage with no state carrying the anchor is refused anchor_not_in_version, not answered');
  }
  const absent = requireList(requireOwn(answer, 'absent_in_states', where), 'absent_in_states')
    .map((state, index) => Object.freeze(readStateRef(state, `absent state ${index + 1}`, workKey)));

  // The states in scope: the rows and the absent states, each once, in date order (then language).
  const scope = [...rows, ...absent].sort((a, b) => (a.applicabilityDate === b.applicabilityDate
    ? (a.language < b.language ? -1 : a.language > b.language ? 1 : 0)
    : (a.applicabilityDate < b.applicabilityDate ? -1 : 1)));
  const digests = new Set();
  for (const state of scope) {
    if (digests.has(state.stateSha256)) throw new Error(`the state ${state.permalink} is listed twice`);
    digests.add(state.stateSha256);
    if (!availableLanguages.includes(state.language)) {
      throw new Error(`a state is in ${state.language}, which the work is not said to be held in`);
    }
    if (language !== null && state.language !== language) {
      throw new Error(`a state is in ${state.language}, and the history was asked in ${language}`);
    }
  }
  for (const list of [rows, absent]) {
    for (let index = 1; index < list.length; index += 1) {
      if (list[index].applicabilityDate < list[index - 1].applicabilityDate) {
        throw new Error('the lineage lists a state before an earlier one; rows and absent states are each in date order');
      }
    }
  }

  // Everything the answer derives from its rows, derived again.
  const previousWording = new Map();
  const runs = new Map();
  const distinct = new Map();
  for (const row of rows) {
    const wording = row.articles.map((article) => article.wordingSha256).join('\n');
    const changed = previousWording.has(row.language) && previousWording.get(row.language) !== wording;
    if (row.wordingChanged !== changed) {
      throw new Error(`the row of ${row.applicabilityDate} in ${row.language} says wording_changed is ${row.wordingChanged}, and its wording ${changed ? 'differs from' : 'is'} the previous row's`);
    }
    if (!previousWording.has(row.language) || changed) runs.set(row.language, (runs.get(row.language) ?? 0) + 1);
    if (!distinct.has(row.language)) distinct.set(row.language, new Set());
    distinct.get(row.language).add(wording);
    previousWording.set(row.language, wording);

    const later = scope
      .filter((other) => other.language === row.language && other.applicabilityDate > row.applicabilityDate)
      .map((other) => other.applicabilityDate)
      .sort()[0] ?? null;
    if (row.nextApplicabilityDate !== later) {
      throw new Error(`the row of ${row.applicabilityDate} in ${row.language} says its next state is from ${row.nextApplicabilityDate}, and the next later state in scope is from ${later}`);
    }
  }
  const statedRuns = readCounts(requireOwn(answer, 'wording_runs', where), 'wording_runs');
  const statedDistinct = readCounts(requireOwn(answer, 'distinct_wordings', where), 'distinct_wordings');
  const same = (stated, derived) => stated.size === derived.size
    && [...derived].every(([key, value]) => stated.get(key) === (value instanceof Set ? value.size : value));
  if (!same(statedRuns, runs)) throw new Error('wording_runs are not the first row and every change of each language');
  if (!same(statedDistinct, distinct)) throw new Error('distinct_wordings are not the distinct wordings of each language');
  const historyBegins = requireDate(requireOwn(answer, 'history_begins', where), 'history_begins');
  if (historyBegins !== rows[0].applicabilityDate) {
    throw new Error(`history_begins is ${historyBegins}, and the lineage's first row is from ${rows[0].applicabilityDate}`);
  }

  return Object.freeze({
    identifier: requireText(requireOwn(answer, 'requested_identifier', where), 'requested_identifier'),
    workKey,
    anchor,
    language,
    availableLanguages,
    historyBegins,
    rows: Object.freeze(rows.map((row) => Object.freeze(row))),
    absent: Object.freeze(absent),
    wordingRuns: Object.freeze(Object.fromEntries(statedRuns)),
    distinctWordings: Object.freeze(Object.fromEntries(statedDistinct)),
    wordingRule: requireText(requireOwn(answer, 'wording_rule', where), 'wording_rule'),
    validityConflictRule: requireText(requireOwn(answer, 'validity_conflict_rule', where), 'validity_conflict_rule'),
    corpusSha256: requireDigest(requireOwn(answer, 'corpus_sha256', where), 'corpus_sha256'),
    indexSha256: requireDigest(requireOwn(answer, 'index_sha256', where), 'index_sha256'),
  });
}
