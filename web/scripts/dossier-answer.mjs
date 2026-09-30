// The V3 Luxembourg dossier answer, read.
//
// The dossier screen's renderer (`dossier.mjs`) was written before V3 against a shape the platform
// does not send: an identity block, dated roles, a status chip and slots. This file reads the answer
// the platform does send for a Luxembourg work (`dossier`, object type `work_record`, publisher
// `lu-legilux`; an EU work's dossier has another shape and is refused here), held to the answer
// captured by driving the real handler (`schemas/v3-platform/answer-samples.json`), and turns it
// into one view a live screen can render. It renders nothing itself.
//
// Every rule here is one the answer states about itself, so an answer that breaks one is drift and
// is thrown, never rendered: the states are the work's own (its work key and publisher work IRI),
// in the language asked for when one was asked, in date order, each pinned by a permalink and a
// stable coordinate that name its work, date and digest; a state's next date is the next later date
// in its language, or null when it is the last; the first and last dates and the count are the
// states'; the titles belong to the states' expressions; and what the dossier does not hold is
// named, each item once, with its reason. The not-held list is the V3 form of the pre-V3 screen's
// unfilled slots, and it is carried whole.

import { isCalendarDate } from './temporal.mjs';

const DIGEST = /^[0-9a-f]{64}$/;

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

function requireTexts(value, where) {
  return Object.freeze(requireList(value, where).map((item, index) => requireText(item, `${where}[${index}]`)));
}

function readState(state, index, workKey, workIri) {
  const where = `state ${index + 1}`;
  const applicabilityDate = requireDate(requireOwn(state, 'applicability_date', where), `${where} applicability_date`);
  const next = requireOwn(state, 'next_applicability_date', where);
  if (next !== null) requireDate(next, `${where} next_applicability_date`);
  const stateSha256 = requireDigest(requireOwn(state, 'state_sha256', where), `${where} state_sha256`);
  const permalink = requireText(requireOwn(state, 'permalink', where), `${where} permalink`);
  const coordinate = requireText(requireOwn(state, 'stable_coordinate', where), `${where} stable_coordinate`);
  if (coordinate !== `/lu-legilux/${workKey}/${applicabilityDate}`) {
    throw new Error(`${where}'s stable coordinate ${coordinate} does not name its work and date`);
  }
  if (permalink !== `${coordinate}--${stateSha256}`) {
    throw new Error(`${where}'s permalink ${permalink} does not pin its coordinate and digest`);
  }
  const publisherWorkIri = requireText(requireOwn(state, 'publisher_work_iri', where), `${where} publisher_work_iri`);
  if (publisherWorkIri !== workIri) {
    throw new Error(`${where} belongs to ${publisherWorkIri}, not to the work ${workIri}`);
  }
  const articleCount = requireCount(requireOwn(state, 'article_count', where), `${where} article_count`);
  if (articleCount === 0) {
    throw new Error(`${where} holds no article; a held state holds at least one`);
  }
  return Object.freeze({
    language: requireText(requireOwn(state, 'language', where), `${where} language`),
    applicabilityDate,
    nextApplicabilityDate: next,
    stateSha256,
    expressionIri: requireText(requireOwn(state, 'expression_iri', where), `${where} expression_iri`),
    publisherLegalResourceIri: requireText(
      requireOwn(state, 'publisher_legal_resource_iri', where), `${where} publisher_legal_resource_iri`),
    articleCount,
    articlesNotAdmitted: requireCount(requireOwn(state, 'articles_not_admitted', where), `${where} articles_not_admitted`),
    stableCoordinate: coordinate,
    permalink,
  });
}

function readTitleEntries(value, where) {
  return Object.freeze(requireList(value, where).map((entry, index) => Object.freeze({
    title: requireText(requireOwn(entry, 'title', `${where}[${index}]`), `${where}[${index}].title`),
    evidenceSha256: requireDigest(requireOwn(entry, 'evidence_sha256', `${where}[${index}]`), `${where}[${index}].evidence_sha256`),
  })));
}

/**
 * Reads one V3 Luxembourg `dossier` answer into the view a live screen renders.
 *
 * @param {object} answer the `result.value` of a dossier envelope
 * @returns {object} a frozen view; throws on any answer the rules above do not allow
 */
export function readDossier(answer) {
  const where = 'this dossier answer';
  const publisher = requireOwn(answer, 'publisher', where);
  if (publisher !== 'lu-legilux') {
    throw new Error(`this dossier answer is ${JSON.stringify(publisher)}'s; this reader reads the Luxembourg (lu-legilux) dossier only`);
  }
  const workKey = requireText(requireOwn(answer, 'work_key', where), 'work_key');
  const workIri = requireText(requireOwn(answer, 'publisher_work_iri', where), 'publisher_work_iri');
  const language = requireOwn(answer, 'requested_language', where);
  if (language !== null) requireText(language, 'requested_language');
  const availableLanguages = requireTexts(requireOwn(answer, 'available_languages', where), 'available_languages');
  if (language !== null && !availableLanguages.includes(language)) {
    throw new Error(`the language asked for, ${language}, is not one the work is held in (${availableLanguages.join(', ')})`);
  }

  const states = requireList(requireOwn(answer, 'states', where), 'states')
    .map((state, index) => readState(state, index, workKey, workIri));
  if (states.length === 0) {
    throw new Error('a dossier is answered for a work with at least one held state');
  }
  for (const [index, state] of states.entries()) {
    if (!availableLanguages.includes(state.language)) {
      throw new Error(`state ${index + 1} is in ${state.language}, which the work is not said to be held in`);
    }
    if (language !== null && state.language !== language) {
      throw new Error(`state ${index + 1} is in ${state.language}, and the dossier was asked in ${language}`);
    }
    if (index > 0 && state.applicabilityDate < states[index - 1].applicabilityDate) {
      throw new Error(`state ${index + 1} (${state.applicabilityDate}) comes after a later state; the states are in date order`);
    }
    const later = states
      .filter((other) => other.language === state.language && other.applicabilityDate > state.applicabilityDate)
      .map((other) => other.applicabilityDate)
      .sort()[0] ?? null;
    if (state.nextApplicabilityDate !== later) {
      throw new Error(
        `state ${index + 1}'s next date is ${state.nextApplicabilityDate}, and the next later state in ${state.language} is ${later}`,
      );
    }
  }
  const seen = new Set();
  for (const state of states) {
    if (seen.has(state.stateSha256)) throw new Error(`the state ${state.permalink} is listed twice`);
    seen.add(state.stateSha256);
  }

  const stateCount = requireCount(requireOwn(answer, 'state_count', where), 'state_count');
  if (stateCount !== states.length) {
    throw new Error(`state_count is ${stateCount}, and the dossier lists ${states.length} states`);
  }
  const historyBegins = requireDate(requireOwn(answer, 'history_begins', where), 'history_begins');
  const latest = requireDate(requireOwn(answer, 'latest_applicability_date', where), 'latest_applicability_date');
  if (historyBegins !== states[0].applicabilityDate || latest !== states[states.length - 1].applicabilityDate) {
    throw new Error('history_begins and latest_applicability_date are the first and last states\' dates');
  }

  const expressions = new Set(states.map((state) => state.expressionIri));
  const titles = Object.freeze(requireList(requireOwn(answer, 'titles', where), 'titles').map((group, index) => {
    const at = `titles[${index}]`;
    const expressionIri = requireText(requireOwn(group, 'expression_iri', at), `${at}.expression_iri`);
    if (!expressions.has(expressionIri)) {
      throw new Error(`${at} is the title of ${expressionIri}, which is not an expression of this dossier's states`);
    }
    const groupLanguage = requireText(requireOwn(group, 'language', at), `${at}.language`);
    if (language !== null && groupLanguage !== language) {
      throw new Error(`${at} is in ${groupLanguage}, and the dossier was asked in ${language}`);
    }
    return Object.freeze({
      language: groupLanguage,
      expressionIri,
      titles: readTitleEntries(requireOwn(group, 'titles', at), `${at}.titles`),
      shortTitles: readTitleEntries(requireOwn(group, 'short_titles', at), `${at}.short_titles`),
    });
  }));

  const items = new Set();
  const notHeld = Object.freeze(requireList(requireOwn(answer, 'not_held', where), 'not_held').map((row, index) => {
    const item = requireText(requireOwn(row, 'item', `not_held[${index}]`), `not_held[${index}].item`);
    if (items.has(item)) throw new Error(`not_held names ${item} twice`);
    items.add(item);
    return Object.freeze({ item, reason: requireText(requireOwn(row, 'reason', `not_held[${index}]`), `not_held[${index}].reason`) });
  }));

  return Object.freeze({
    identifier: requireText(requireOwn(answer, 'requested_identifier', where), 'requested_identifier'),
    workKey,
    publisherWorkIri: workIri,
    language,
    availableLanguages,
    titles,
    stateCount,
    historyBegins,
    latestApplicabilityDate: latest,
    states: Object.freeze(states),
    notHeld,
    scope: requireText(requireOwn(answer, 'scope', where), 'scope'),
    articlesNotAdmittedNote: requireText(requireOwn(answer, 'articles_not_admitted_note', where), 'articles_not_admitted_note'),
    corpusSha256: requireDigest(requireOwn(answer, 'corpus_sha256', where), 'corpus_sha256'),
    indexSha256: requireDigest(requireOwn(answer, 'index_sha256', where), 'index_sha256'),
  });
}
