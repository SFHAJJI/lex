// The V3 dossier answers, read: a Luxembourg work's, and an EU work's.
//
// The dossier screen's renderer (`dossier.mjs`) was written before V3 against a shape the platform
// does not send: an identity block, dated roles, a status chip and slots. This file reads the answers
// the platform does send (`dossier`, object type `work_record`), held to the answers captured by
// driving the real handler (`schemas/v3-platform/answer-samples.json`), and turns each into one view
// a live screen can render. It renders nothing itself. A Luxembourg work's dossier (publisher
// `lu-legilux`) is read by `readDossier`; an EU work's (publisher `eu-eurlex`) has another shape and
// is read by `readEuropeDossier`; `readDossierAnswer` sends an answer to the one its publisher names.
//
// Every rule here is one the answer states about itself, so an answer that breaks one is drift and
// is thrown, never rendered: the states are the work's own (its work key and publisher work IRI),
// in the language asked for when one was asked, in date order, each pinned by a permalink and a
// stable coordinate that name its work, date and digest; a state's next date is the next later date
// in its language, or null when it is the last; the first and last dates and the count are the
// states'; the titles belong to the states' expressions; and what the dossier does not hold is
// named, each item once, with its reason. The not-held list is the V3 form of the pre-V3 screen's
// unfilled slots, and it is carried whole.
//
// An EU dossier states its own rules: every expression is of the work and in a language it is held
// in (the one asked, when one was asked), listed once, counted, resolvable by its own IRI, and pinned
// by the one wording held of it, as EU search pins it: the permalink
// `/eu-eurlex/{celex}/{language}/{wording date}--{wording sha256}`, whose date is the expression's one
// wording date. An expression that pins no wording is refused here rather than shown unpinned.

import { isCalendarDate } from './temporal.mjs';
import { europePermalink, luxembourgPermalink } from './permalinks.mjs';
import { readAnnexesNotServed } from './europe-annexes.mjs';
import { readAsPublished } from './as-published.mjs';
import { readCatalogueFacts, readDateFacts } from './date-role.mjs';
import { readEuropeForceDates } from './europe-force-dates.mjs';

/** The text statuses a version of the catalogue held without text names (index 9's `text_status`, S7a). */
export const TEXTLESS_STATUSES = Object.freeze([
  'withheld_rights', 'not_available_pdf', 'not_available_derivation', 'not_available_misfiled', 'not_acquired',
]);

/**
 * The publisher catalogue's chain of a work on lex-v3-luxembourg-index/9 (S7b; the platform's `catalogue_chain_rule`), or
 * null on an answer that names none (an index of schema 8 or 6): each version of the catalogue whose text is not held (its
 * kind, its date's role, why, and its permalink or, when the catalogue names no expression of it, its stable coordinate),
 * then the chain's gaps, overlaps, same-date groups, unclear end days and invalid intervals. Each date is a calendar date and
 * each span runs forward; a version listed here is never one of the held states.
 */
function readCatalogueChain(answer, workKey, heldDigests) {
  if (!Object.hasOwn(answer, 'catalogue_chain_rule')) return null;
  const where = 'the catalogue chain';
  const span = (entry, at) => {
    const from = requireDate(requireOwn(entry, 'from', at), `${at}.from`);
    const to = requireDate(requireOwn(entry, 'to', at), `${at}.to`);
    if (to <= from) throw new Error(`${at} runs from ${from} to ${to}`);
    return { from, to };
  };
  const textless = Object.freeze(requireList(requireOwn(answer, 'textless_states', where), 'textless_states').map((row, index) => {
    const at = `textless_states[${index}]`;
    const applicabilityDate = requireDate(requireOwn(row, 'applicability_date', at), `${at}.applicability_date`);
    const stateSha256 = requireDigest(requireOwn(row, 'state_sha256', at), `${at}.state_sha256`);
    if (heldDigests.has(stateSha256)) throw new Error(`${at} is a held state, listed as held without text`);
    const textStatus = requireOwn(row, 'text_status', at);
    if (!TEXTLESS_STATUSES.includes(textStatus)) {
      throw new Error(`${at} is held without text for ${JSON.stringify(textStatus)}; a version without text is ${TEXTLESS_STATUSES.join(', ')}`);
    }
    const coordinate = requireText(requireOwn(row, 'stable_coordinate', at), `${at}.stable_coordinate`);
    if (coordinate !== `/lu-legilux/${workKey}/${applicabilityDate}`) throw new Error(`${at}'s stable coordinate ${coordinate} does not name its work and date`);
    const language = requireOwn(row, 'language', at);
    const permalink = requireOwn(row, 'permalink', at);
    if ((language === null) !== (permalink === null)) throw new Error(`${at} has a permalink exactly when it names its expression's language`);
    if (permalink !== null && permalink !== luxembourgPermalink({ workKey, date: applicabilityDate, stateSha256, language: requireText(language, `${at}.language`) })) {
      throw new Error(`${at}'s permalink ${permalink} does not pin its coordinate, digest and language`);
    }
    const facts = readCatalogueFacts(row, applicabilityDate, at);
    if (facts.stateKind === null) throw new Error(`${at} names no state_kind`);
    return Object.freeze({ applicabilityDate, stateSha256, language, permalink, stableCoordinate: coordinate, textStatus, ...facts });
  }));
  const gaps = Object.freeze(requireList(requireOwn(answer, 'gaps', where), 'gaps').map((entry, index) => Object.freeze(span(entry, `gaps[${index}]`))));
  const overlaps = Object.freeze(requireList(requireOwn(answer, 'overlaps', where), 'overlaps').map((entry, index) => {
    const at = `overlaps[${index}]`;
    const states = requireList(requireOwn(entry, 'state_sha256s', at), `${at}.state_sha256s`);
    if (states.length !== 2) throw new Error(`${at} names ${states.length} states; an overlap is two states' span`);
    return Object.freeze({ ...span(entry, at), count: states.length });
  }));
  const sameDate = Object.freeze(requireList(requireOwn(answer, 'same_date', where), 'same_date').map((entry, index) => {
    const at = `same_date[${index}]`;
    const states = requireList(requireOwn(entry, 'state_sha256s', at), `${at}.state_sha256s`);
    if (states.length < 2) throw new Error(`${at} groups ${states.length} state; a same-date group is two states or more`);
    states.forEach((digest, position) => requireDigest(digest, `${at}.state_sha256s[${position}]`));
    return Object.freeze({ date: requireDate(requireOwn(entry, 'applicability_date', at), `${at}.applicability_date`), count: states.length });
  }));
  const unclearEndDays = Object.freeze(requireList(requireOwn(answer, 'unclear_end_days', where), 'unclear_end_days').map((entry, index) =>
    Object.freeze({ date: requireDate(requireOwn(entry, 'date', `unclear_end_days[${index}]`), `unclear_end_days[${index}].date`) })));
  const invalidIntervals = Object.freeze(requireList(requireOwn(answer, 'invalid_intervals', where), 'invalid_intervals').map((entry, index) => {
    const at = `invalid_intervals[${index}]`;
    const date = requireDate(requireOwn(entry, 'applicability_date', at), `${at}.applicability_date`);
    const to = requireDate(requireOwn(entry, 'applicable_to', at), `${at}.applicable_to`);
    if (to > date) throw new Error(`${at} ends on ${to}, after its start ${date}; an invalid interval ends on or before it`);
    return Object.freeze({ date, to });
  }));
  // Every transition is read for its shape, so a kind the chain does not name is refused rather than dropped.
  const kinds = ['same_date', 'contiguous', 'derived', 'inclusive_suspected', 'hole', 'overlap', 'invalid'];
  requireList(requireOwn(answer, 'transitions', where), 'transitions').forEach((entry, index) => {
    if (!kinds.includes(requireOwn(entry, 'kind', `transitions[${index}]`))) {
      throw new Error(`transitions[${index}] is of the kind ${JSON.stringify(entry.kind)}; a transition is ${kinds.join(', ')}`);
    }
  });
  return Object.freeze({
    textless, gaps, overlaps, sameDate, unclearEndDays, invalidIntervals,
    rule: requireText(requireOwn(answer, 'catalogue_chain_rule', where), 'catalogue_chain_rule'),
  });
}

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

function requireBoolean(value, where) {
  if (typeof value !== 'boolean') {
    throw new Error(`${where} is true or false, not ${JSON.stringify(value)}`);
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
  const language = requireText(requireOwn(state, 'language', where), `${where} language`);
  if (permalink !== luxembourgPermalink({ workKey, date: applicabilityDate, stateSha256, language })) {
    throw new Error(`${where}'s permalink ${permalink} does not pin its coordinate, digest and language`);
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
    language,
    applicabilityDate,
    // The date's role and the act's entry into force (index 9; null on an index of schema 8 or 6): R21, `date-role.mjs`.
    ...readDateFacts(state, applicabilityDate, where),
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

  const notHeld = readNotHeld(requireOwn(answer, 'not_held', where));

  return Object.freeze({
    publisher,
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
    // An act held only as first published says so (the plan's item 0.6): `as-published.mjs` reads it, or null.
    asPublished: readAsPublished(answer, { states, workIri, notHeld }),
    // Index 9's catalogue chain (S7b), or null on an index of schema 8 or 6.
    chain: readCatalogueChain(answer, workKey, seen),
    scope: requireText(requireOwn(answer, 'scope', where), 'scope'),
    articlesNotAdmittedNote: requireText(requireOwn(answer, 'articles_not_admitted_note', where), 'articles_not_admitted_note'),
    corpusSha256: requireDigest(requireOwn(answer, 'corpus_sha256', where), 'corpus_sha256'),
    indexSha256: requireDigest(requireOwn(answer, 'index_sha256', where), 'index_sha256'),
  });
}

function readNotHeld(value) {
  const items = new Set();
  return Object.freeze(requireList(value, 'not_held').map((row, index) => {
    const item = requireText(requireOwn(row, 'item', `not_held[${index}]`), `not_held[${index}].item`);
    if (items.has(item)) throw new Error(`not_held names ${item} twice`);
    items.add(item);
    return Object.freeze({ item, reason: requireText(requireOwn(row, 'reason', `not_held[${index}]`), `not_held[${index}].reason`) });
  }));
}

function readEuropeExpression(expression, index, celex) {
  const where = `expression ${index + 1}`;
  const expressionIri = requireText(requireOwn(expression, 'publisher_expression_id', where), `${where} publisher_expression_id`);
  const language = requireText(requireOwn(expression, 'language', where), `${where} language`);
  const wordingDates = Object.freeze(requireList(requireOwn(expression, 'wording_dates', where), `${where} wording_dates`)
    .map((date, at) => requireDate(date, `${where} wording_dates[${at}]`)));
  const coordinate = requireText(requireOwn(requireOwn(expression, 'resolve', where), 'identifier', `${where} resolve`), `${where} resolve.identifier`);
  if (coordinate !== expressionIri) {
    throw new Error(`${where} resolves to ${coordinate}, not to the expression it names (${expressionIri})`);
  }

  // The one wording held, pinned as EU search pins it. A dossier this page shows cites every expression by
  // its pinned wording, so an expression without one is not shown unpinned.
  const pinned = requireOwn(expression, 'pinned_wording', where);
  if (pinned === null) {
    throw new Error(`${where} pins no wording (its pinned_wording is null), and an expression this page shows must pin its wording`);
  }
  const wordingDate = requireDate(requireOwn(pinned, 'wording_date', `${where} pinned_wording`), `${where} pinned_wording.wording_date`);
  const wordingSha256 = requireDigest(requireOwn(pinned, 'wording_sha256', `${where} pinned_wording`), `${where} pinned_wording.wording_sha256`);
  const permalink = requireText(requireOwn(pinned, 'permalink', `${where} pinned_wording`), `${where} pinned_wording.permalink`);
  if (wordingDates.length !== 1 || wordingDates[0] !== wordingDate) {
    throw new Error(`${where} pins the wording of ${wordingDate}, and its wording dates are ${JSON.stringify(wordingDates)}; one wording is pinned exactly when one is held`);
  }
  const expected = europePermalink({ celex, date: wordingDate, wordingSha256, language });
  if (permalink !== expected) {
    throw new Error(`${where} carries the permalink ${JSON.stringify(permalink)}, not the wording it pins (${expected})`);
  }

  return Object.freeze({
    expressionIri,
    language,
    wordingDates,
    articleCount: requireCount(requireOwn(expression, 'article_count', where), `${where} article_count`),
    members: Object.freeze(requireList(requireOwn(expression, 'members', where), `${where} members`).map((member, at) => Object.freeze({
      objectRefSha256: requireDigest(requireOwn(member, 'object_ref_sha256', `${where} members[${at}]`), `${where} members[${at}].object_ref_sha256`),
      outcome: requireText(requireOwn(member, 'outcome', `${where} members[${at}]`), `${where} members[${at}].outcome`),
      contentClass: requireText(requireOwn(member, 'content_class', `${where} members[${at}]`), `${where} members[${at}].content_class`),
    }))),
    coordinate,
    wording: Object.freeze({ wordingDate, wordingSha256, permalink }),
    // The annexes of its members, never served as text: each disposition's count, reason and official source.
    annexesNotServed: readAnnexesNotServed(expression, where),
  });
}

/**
 * Reads one V3 EU `dossier` answer into the view a live screen renders: the work (its CELEX and work
 * IRI), every expression the EU index holds of it with the one wording held of each, pinned, and what
 * the dossier does not hold. The wording date is the date the publisher's Formex package gives the
 * act (`date_semantics`), never an applicability date.
 *
 * @param {object} answer the `result.value` of a dossier envelope
 * @returns {object} a frozen view; throws on any answer the rules above do not allow
 */
export function readEuropeDossier(answer) {
  const where = 'this dossier answer';
  const publisher = requireOwn(answer, 'publisher', where);
  if (publisher !== 'eu-eurlex') {
    throw new Error(`this dossier answer is ${JSON.stringify(publisher)}'s; this reader reads the EU (eu-eurlex) dossier only`);
  }
  const celex = requireText(requireOwn(answer, 'celex', where), 'celex');
  const language = requireOwn(answer, 'requested_language', where);
  if (language !== null) requireText(language, 'requested_language');
  const availableLanguages = requireTexts(requireOwn(answer, 'available_languages', where), 'available_languages');
  if (language !== null && !availableLanguages.includes(language)) {
    throw new Error(`the language asked for, ${language}, is not one the work is held in (${availableLanguages.join(', ')})`);
  }

  const expressions = requireList(requireOwn(answer, 'expressions', where), 'expressions')
    .map((expression, index) => readEuropeExpression(expression, index, celex));
  if (expressions.length === 0) {
    throw new Error('a dossier is answered for a work with at least one held expression');
  }
  const seen = new Set();
  for (const [index, expression] of expressions.entries()) {
    if (!availableLanguages.includes(expression.language)) {
      throw new Error(`expression ${index + 1} is in ${expression.language}, which the work is not said to be held in`);
    }
    if (language !== null && expression.language !== language) {
      throw new Error(`expression ${index + 1} is in ${expression.language}, and the dossier was asked in ${language}`);
    }
    if (seen.has(expression.expressionIri)) throw new Error(`the expression ${expression.expressionIri} is listed twice`);
    seen.add(expression.expressionIri);
  }
  const expressionCount = requireCount(requireOwn(answer, 'expression_count', where), 'expression_count');
  if (expressionCount !== expressions.length) {
    throw new Error(`expression_count is ${expressionCount}, and the dossier lists ${expressions.length} expressions`);
  }

  return Object.freeze({
    publisher,
    identifier: requireText(requireOwn(answer, 'requested_identifier', where), 'requested_identifier'),
    celex,
    publisherWorkIri: requireText(requireOwn(answer, 'publisher_work_id', where), 'publisher_work_id'),
    language,
    availableLanguages,
    expressionCount,
    expressions: Object.freeze(expressions),
    digestRule: requireText(requireOwn(answer, 'digest_rule', where), 'digest_rule'),
    dateSemantics: requireText(requireOwn(answer, 'date_semantics', where), 'date_semantics'),
    consolidationsHeld: requireBoolean(requireOwn(answer, 'consolidations_held', where), 'consolidations_held'),
    // The act's dates as the publisher records them, over an EU index /6; null over an older one (`europe-force-dates.mjs`).
    forceDates: readEuropeForceDates(answer, where),
    notHeld: readNotHeld(requireOwn(answer, 'not_held', where)),
    scope: requireText(requireOwn(answer, 'scope', where), 'scope'),
    corpusSha256: requireDigest(requireOwn(answer, 'corpus_sha256', where), 'corpus_sha256'),
    indexSha256: requireDigest(requireOwn(answer, 'index_sha256', where), 'index_sha256'),
  });
}

/** Reads a dossier answer with the reader its publisher names: Luxembourg's, or the EU's. */
export function readDossierAnswer(answer) {
  return answer !== null && typeof answer === 'object' && answer.publisher === 'eu-eurlex'
    ? readEuropeDossier(answer)
    : readDossier(answer);
}
