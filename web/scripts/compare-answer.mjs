// The V3 Luxembourg comparison, read.
//
// The compare screen's renderer (`compare.mjs`) was written before V3 against a shape the platform
// does not send. This file reads the `diff` answer (object type `diff`, publisher `lu-legilux`), held
// to the two answers captured by driving the real handler (`schemas/v3-platform/answer-samples.json`:
// one state against itself, and two states with one article reworded and one id renamed), and turns
// it into one view a live screen can render. It renders nothing itself.
//
// A comparison is article by article, by the publisher's article id and the wording digests on each
// side, and says nothing about legal effect. Everything it derives the reader derives again, and it
// throws where they disagree:
//  - each side is the state that applies on its date (from on or before it, to a next date after it
//    or none), pinned by a permalink and a stable coordinate that name its work, date and digest, with
//    its validity flags and their count following its dates;
//  - one comparison per compared language and one entry per language not compared, each language
//    once and in the languages' order, in the language asked when one was asked;
//  - the same state on both dates is said as such, with no articles and no counts;
//  - otherwise every article of each side appears in exactly one row, the rows are in the ids'
//    ordinal order, and each row's status is its sides': added, removed, unchanged when the ordered
//    wording digests are equal, changed when they are not; the counts are the rows'.

import { isCalendarDate } from './temporal.mjs';

const DIGEST = /^[0-9a-f]{64}$/;

/** The four statuses a compared article can have, as the platform names them. */
export const ARTICLE_STATUSES = Object.freeze(['unchanged', 'changed', 'added', 'removed']);

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

/** One side of a comparison: the state that applies on the date asked, as `as_of` states it. */
function readSide(side, where, context) {
  const language = requireText(requireOwn(side, 'language', where), `${where} language`);
  if (language !== context.language) {
    throw new Error(`${where} is in ${language}, in the comparison of ${context.language}`);
  }
  const applicabilityDate = requireDate(requireOwn(side, 'applicability_date', where), `${where} applicability_date`);
  if (applicabilityDate > context.date) {
    throw new Error(`${where} applies from ${applicabilityDate}, after its date ${context.date}`);
  }
  const next = requireOwn(side, 'next_applicability_date', where);
  if (next !== null && requireDate(next, `${where} next_applicability_date`) <= context.date) {
    throw new Error(`${where} is followed from ${next}, so it does not apply on its date ${context.date}`);
  }
  const stateSha256 = requireDigest(requireOwn(side, 'state_sha256', where), `${where} state_sha256`);
  const coordinate = requireText(requireOwn(side, 'stable_coordinate', where), `${where} stable_coordinate`);
  if (coordinate !== `/lu-legilux/${context.workKey}/${applicabilityDate}`) {
    throw new Error(`${where}'s stable coordinate ${coordinate} does not name its work and date`);
  }
  const permalink = requireText(requireOwn(side, 'permalink', where), `${where} permalink`);
  if (permalink !== `${coordinate}--${stateSha256}`) {
    throw new Error(`${where}'s permalink ${permalink} does not pin its coordinate and digest`);
  }
  const identities = requireList(requireOwn(side, 'article_identities', where), `${where} article_identities`)
    .map((identity, index) => requireDigest(identity, `${where} article_identities[${index}]`));
  const articles = requireList(requireOwn(side, 'articles', where), `${where} articles`).map((article, index) => {
    const at = `${where} article ${index + 1}`;
    const validFrom = requireOwn(article, 'article_valid_from', at);
    if (validFrom !== null) requireDate(validFrom, `${at} article_valid_from`);
    const conflict = requireBoolean(requireOwn(article, 'validity_conflict', at), `${at} validity_conflict`);
    if (conflict !== (validFrom !== null && validFrom !== applicabilityDate)) {
      throw new Error(`${at} says validity_conflict is ${conflict} for its date ${validFrom} in a state of ${applicabilityDate}`);
    }
    return { identity: requireDigest(requireOwn(article, 'article_identity_sha256', at), `${at} article_identity_sha256`), conflict };
  });
  const conflictCount = requireCount(requireOwn(side, 'validity_conflict_count', where), `${where} validity_conflict_count`);
  if (conflictCount !== articles.filter((article) => article.conflict).length) {
    throw new Error(`${where}'s validity_conflict_count is ${conflictCount}, and its articles say otherwise`);
  }
  return Object.freeze({
    language,
    applicabilityDate,
    nextApplicabilityDate: next,
    stateSha256,
    stableCoordinate: coordinate,
    permalink,
    expressionIri: requireText(requireOwn(side, 'expression_iri', where), `${where} expression_iri`),
    articleIdentities: Object.freeze(identities),
    articleCount: identities.length,
    articlesNotAdmitted: requireCount(requireOwn(side, 'articles_not_admitted', where), `${where} articles_not_admitted`),
    validityConflictCount: conflictCount,
  });
}

function readEntries(value, where) {
  return Object.freeze(requireList(value, where).map((entry, index) => Object.freeze({
    articleIdentitySha256: requireDigest(requireOwn(entry, 'article_identity_sha256', `${where}[${index}]`), `${where}[${index}].article_identity_sha256`),
    wordingSha256: requireDigest(requireOwn(entry, 'wording_sha256', `${where}[${index}]`), `${where}[${index}].wording_sha256`),
  })));
}

function statusOf(from, to) {
  if (from.length === 0) return 'added';
  if (to.length === 0) return 'removed';
  const wording = (side) => side.map((entry) => entry.wordingSha256).join('\n');
  return wording(from) === wording(to) ? 'unchanged' : 'changed';
}

function readComparison(comparison, index, context) {
  const where = `comparison ${index + 1}`;
  const language = requireText(requireOwn(comparison, 'language', where), `${where} language`);
  const from = readSide(requireOwn(comparison, 'from', where), `${where} from`, { ...context, language, date: context.dateFrom });
  const to = readSide(requireOwn(comparison, 'to', where), `${where} to`, { ...context, language, date: context.dateTo });
  const sameState = requireBoolean(requireOwn(comparison, 'same_state', where), `${where} same_state`);
  if (sameState !== (from.stateSha256 === to.stateSha256)) {
    throw new Error(`${where} says same_state is ${sameState}, and its sides are ${from.stateSha256 === to.stateSha256 ? 'one state' : 'two states'}`);
  }
  const note = requireText(requireOwn(comparison, 'note', where), `${where} note`);
  const articlesValue = requireOwn(comparison, 'articles', where);
  const countsValue = requireOwn(comparison, 'counts', where);
  if (sameState) {
    if (articlesValue !== null || countsValue !== null) {
      throw new Error(`${where} is the same state on both dates, which compares no article and counts none`);
    }
    return Object.freeze({ language, from, to, sameState, note, articles: null, counts: null });
  }

  const rows = requireList(articlesValue, `${where} articles`).map((row, rowIndex) => {
    const at = `${where} row ${rowIndex + 1}`;
    const publisherId = requireText(requireOwn(row, 'publisher_id', at), `${at} publisher_id`);
    const rowFrom = readEntries(requireOwn(row, 'from', at), `${at} from`);
    const rowTo = readEntries(requireOwn(row, 'to', at), `${at} to`);
    if (rowFrom.length === 0 && rowTo.length === 0) throw new Error(`${at} (${publisherId}) is on neither side`);
    const status = requireOwn(row, 'status', at);
    if (status !== statusOf(rowFrom, rowTo)) {
      throw new Error(`${at} (${publisherId}) says ${JSON.stringify(status)}, and its sides make it ${statusOf(rowFrom, rowTo)}`);
    }
    return Object.freeze({ publisherId, status, from: rowFrom, to: rowTo });
  });
  for (let rowIndex = 1; rowIndex < rows.length; rowIndex += 1) {
    if (!(rows[rowIndex - 1].publisherId < rows[rowIndex].publisherId)) {
      throw new Error(`${where} lists ${rows[rowIndex].publisherId} after ${rows[rowIndex - 1].publisherId}; the rows are the ids in ordinal order, each once`);
    }
  }
  for (const [side, entries] of [[from, rows.flatMap((row) => row.from)], [to, rows.flatMap((row) => row.to)]]) {
    const listed = entries.map((entry) => entry.articleIdentitySha256).sort();
    const held = [...side.articleIdentities].sort();
    if (listed.length !== held.length || listed.some((identity, position) => identity !== held[position])) {
      throw new Error(`${where}'s rows do not cover the articles of ${side.permalink} exactly once each`);
    }
  }
  const counts = Object.fromEntries(ARTICLE_STATUSES.map((status) => [
    status, requireCount(requireOwn(countsValue, status, `${where} counts`), `${where} counts.${status}`),
  ]));
  for (const status of ARTICLE_STATUSES) {
    const tallied = rows.filter((row) => row.status === status).length;
    if (counts[status] !== tallied) {
      throw new Error(`${where} counts ${counts[status]} ${status}, and ${tallied} of its rows are`);
    }
  }
  return Object.freeze({ language, from, to, sameState, note, articles: Object.freeze(rows), counts: Object.freeze(counts) });
}

/**
 * Reads one V3 Luxembourg `diff` answer into the view a live screen renders.
 *
 * @param {object} answer the `result.value` of a diff envelope
 * @returns {object} a frozen view; throws on any answer the rules above do not allow
 */
export function readDiff(answer) {
  const where = 'this comparison';
  const publisher = requireOwn(answer, 'publisher', where);
  if (publisher !== 'lu-legilux') {
    throw new Error(`this comparison is ${JSON.stringify(publisher)}'s; this reader reads the Luxembourg (lu-legilux) comparison only`);
  }
  const workKey = requireText(requireOwn(answer, 'work_key', where), 'work_key');
  const dateFrom = requireDate(requireOwn(answer, 'requested_date_from', where), 'requested_date_from');
  const dateTo = requireDate(requireOwn(answer, 'requested_date_to', where), 'requested_date_to');
  const language = requireTextOrNull(requireOwn(answer, 'requested_language', where), 'requested_language');
  const availableLanguages = Object.freeze(requireList(requireOwn(answer, 'available_languages', where), 'available_languages')
    .map((item, index) => requireText(item, `available_languages[${index}]`)));
  if (language !== null && !availableLanguages.includes(language)) {
    throw new Error(`the language asked for, ${language}, is not one the work is held in`);
  }

  const comparisons = requireList(requireOwn(answer, 'comparisons', where), 'comparisons')
    .map((comparison, index) => readComparison(comparison, index, { workKey, dateFrom, dateTo }));
  if (comparisons.length === 0) {
    throw new Error('a comparison with nothing compared is refused no_version_for_date, not answered');
  }
  const notCompared = Object.freeze(requireList(requireOwn(answer, 'languages_not_compared', where), 'languages_not_compared')
    .map((entry, index) => {
      const at = `languages_not_compared[${index}]`;
      const bound = requireOwn(entry, 'bound', at);
      if (bound !== 'from' && bound !== 'to') throw new Error(`${at} misses the bound ${JSON.stringify(bound)}; a bound is from or to`);
      return Object.freeze({
        language: requireText(requireOwn(entry, 'language', at), `${at}.language`),
        bound,
        reason: requireText(requireOwn(entry, 'reason', at), `${at}.reason`),
      });
    }));
  const seen = new Set();
  for (const item of [...comparisons, ...notCompared]) {
    if (seen.has(item.language)) throw new Error(`${item.language} is compared or listed as not compared twice`);
    seen.add(item.language);
    if (!availableLanguages.includes(item.language)) {
      throw new Error(`${item.language} is not a language the work is held in`);
    }
    if (language !== null && item.language !== language) {
      throw new Error(`${item.language} is in the answer, and the comparison was asked in ${language}`);
    }
  }
  for (const list of [comparisons, notCompared]) {
    for (let index = 1; index < list.length; index += 1) {
      if (!(list[index - 1].language < list[index].language)) {
        throw new Error('the languages are in their ordinal order');
      }
    }
  }

  return Object.freeze({
    identifier: requireText(requireOwn(answer, 'requested_identifier', where), 'requested_identifier'),
    workKey,
    dateFrom,
    dateTo,
    language,
    availableLanguages,
    comparisons: Object.freeze(comparisons),
    languagesNotCompared: notCompared,
    wordingRule: requireText(requireOwn(answer, 'wording_rule', where), 'wording_rule'),
    validityConflictRule: requireText(requireOwn(answer, 'validity_conflict_rule', where), 'validity_conflict_rule'),
    articlesNotAdmittedNote: requireText(requireOwn(answer, 'articles_not_admitted_note', where), 'articles_not_admitted_note'),
    corpusSha256: requireDigest(requireOwn(answer, 'corpus_sha256', where), 'corpus_sha256'),
    indexSha256: requireDigest(requireOwn(answer, 'index_sha256', where), 'index_sha256'),
  });
}
