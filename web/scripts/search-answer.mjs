// The V3 search answers, read: Luxembourg's, and the EU's in one work.
//
// The search screen's renderer (`search-results.mjs`) was written before V3 against a shape the
// platform no longer sends: `lex_id`, `provision_num`, a row set and four match reasons. This file
// reads the answers the platform does send, held to the answers captured by driving the real handler
// (`schemas/v3-platform/answer-samples.json`, operation `search`), and turns each into one view a
// live screen can render. It renders nothing itself. A Luxembourg search (`publisher: "lu-legilux"`)
// is read by `readSearch`; an EU search, answered for one named EU work in the one wording the EU
// index holds of it (`publisher: "eu-eurlex"`), has another shape and is read by `readEuropeSearch`;
// `readSearchAnswer` sends an answer to the one its publisher names.
//
// Every rule here is one the answer states about itself, so an answer that breaks one is drift and
// is thrown, never rendered: the lanes (strict before relaxed, a hit's reason is its lane's, and
// with a mode every hit is in that lane and the other lane is not counted, null rather than zero);
// the population, which counts the whole result and not the page (an untruncated first page holds
// all of it; a page after a cursor or before one holds less); the page (a cursor exactly when the
// page is truncated, naming its last hit); each hit's permalink (the work, the date and the state
// digest the hit names); the works a dated search could not place (named with their candidate
// states, contributing no hit); and how the query was resolved to a work, if it was. A search answer
// carries no text snippet, so none is read.
//
// An EU answer states the same lane, page and population rules, and its own: it is in one work, one
// expression and one language, it names no date (`requested_date` is null and no work is ambiguous),
// and every hit is in the one wording the answer pins (`pinned_wording`): the wording's permalink,
// `/eu-eurlex/{celex}/{language}/{wording date}--{wording sha256}`, and each hit's permalink is it
// with the provision after `#`. A hit this page shows must pin its wording, so an answer with hits
// and no pinned wording is refused here rather than shown unpinned. The wording date is the date the
// publisher's Formex package gives the act (`date_semantics`), never an applicability date.

import { isCalendarDate } from './temporal.mjs';

const DIGEST = /^[0-9a-f]{64}$/;

/** The two lanes the index holds, in the order a page serves them. */
export const SEARCH_LANES = Object.freeze(['strict', 'relaxed']);

/** A hit's one match reason is its lane's: the phrase as typed, or every term in any order. */
export const LANE_MATCH_REASON = Object.freeze({ strict: 'exact_phrase', relaxed: 'all_terms' });

/** How the query was resolved to a work (lane r1), in the platform's closed vocabulary. */
export const WORK_RESOLUTION_OUTCOMES = Object.freeze([
  'not_run_identifier_given',
  'no_titles_held',
  'no_title_match',
  'one_work',
  'several_candidates',
]);

const CURSOR = /^(strict|relaxed)\.[0-9a-f]{64}\.[0-9a-f]{64}$/;
const STATE_PERMALINK = /^\/lu-legilux\/([^/]+)\/(\d{4}-\d{2}-\d{2})--[0-9a-f]{64}$/;

/** An EU page's cursor names the lane and the article identity: an EU search is in one wording, so no state is named. */
const EUROPE_CURSOR = /^(strict|relaxed)\.[0-9a-f]{64}$/;

/** The EU wording permalink: the CELEX, the language, the wording date and the wording digest. */
const EUROPE_WORDING_PERMALINK = /^\/eu-eurlex\/([^/#]+)\/([^/#]+)\/(\d{4}-\d{2}-\d{2})--([0-9a-f]{64})$/;

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

function requireBoolean(value, where) {
  if (typeof value !== 'boolean') {
    throw new Error(`${where} is true or false, not ${JSON.stringify(value)}`);
  }
  return value;
}

function requireDate(value, where) {
  if (!isCalendarDate(value)) {
    throw new Error(`${where} is not a calendar date: ${JSON.stringify(value)}`);
  }
  return value;
}

function requireTexts(value, where, { atLeast = 0 } = {}) {
  const list = requireList(value, where).map((item, index) => requireText(item, `${where}[${index}]`));
  if (list.length < atLeast) {
    throw new Error(`${where} names at least ${atLeast}`);
  }
  return Object.freeze(list);
}

/** A lane's count: null exactly when a mode asked for the other lane alone, which was then not scanned. */
function readLaneCount(population, lane, mode) {
  const key = `${lane}_hits`;
  const value = requireOwn(population, key, 'population');
  const scanned = mode === null || mode === lane;
  if (!scanned) {
    if (value !== null) {
      throw new Error(`population.${key} is ${JSON.stringify(value)} for a search in the ${mode} lane alone; a lane not asked for is not counted, null rather than zero`);
    }
    return null;
  }
  return requireCount(value, `population.${key}`);
}

function readWorkCard(card, where) {
  return Object.freeze({
    workIdentifier: requireText(requireOwn(card, 'work_identifier', where), `${where} work_identifier`),
    expressions: requireTexts(requireOwn(card, 'expressions', where), `${where} expressions`, { atLeast: 1 }),
    languages: requireTexts(requireOwn(card, 'languages', where), `${where} languages`),
    matchedTitle: requireText(requireOwn(card, 'matched_title', where), `${where} matched_title`),
    // The title's own language: the resolver searches every language's titles (review of #797).
    matchedTitleLanguage: requireText(requireOwn(card, 'matched_title_language', where), `${where} matched_title_language`),
    matchReason: requireText(requireOwn(card, 'match_reason', where), `${where} match_reason`),
  });
}

function readWorkResolution(value, identifier) {
  const where = 'work_resolution';
  if (requireOwn(value, 'retrieval_lane', where) !== 'r1_work_discovery') {
    throw new Error('work_resolution is lane r1_work_discovery');
  }
  const outcome = requireOwn(value, 'outcome', where);
  if (!WORK_RESOLUTION_OUTCOMES.includes(outcome)) {
    throw new Error(`work_resolution.outcome ${JSON.stringify(outcome)} is not one of ${WORK_RESOLUTION_OUTCOMES.join(', ')}`);
  }
  if ((identifier !== null) !== (outcome === 'not_run_identifier_given')) {
    throw new Error('the work is resolved from the query exactly when no identifier is given');
  }
  const work = requireOwn(value, 'work', where);
  const candidates = requireOwn(value, 'candidates', where);
  if ((work !== null) !== (outcome === 'one_work')) {
    throw new Error('work_resolution.work is the one work exactly when the outcome is one_work');
  }
  if ((candidates !== null) !== (outcome === 'several_candidates')) {
    throw new Error('work_resolution.candidates are listed exactly when the outcome is several_candidates');
  }
  const cards = candidates === null
    ? null
    : requireList(candidates, 'work_resolution.candidates').map((card, index) => readWorkCard(card, `work_resolution.candidates[${index}]`));
  if (cards !== null && cards.length < 2) {
    throw new Error('several candidates are at least two');
  }
  return Object.freeze({
    outcome,
    work: work === null ? null : readWorkCard(work, 'work_resolution.work'),
    candidates: cards === null ? null : Object.freeze(cards),
  });
}

function readAmbiguousWorks(value, requestedDate, hits) {
  const works = requireList(value, 'ambiguous_works');
  if (works.length > 0 && requestedDate === null) {
    throw new Error('a work is ambiguous only on a date, and this search asked for none');
  }
  const seen = new Set();
  return Object.freeze(works.map((entry, index) => {
    const where = `ambiguous_works[${index}]`;
    const workKey = requireText(requireOwn(entry, 'work_key', where), `${where} work_key`);
    if (seen.has(workKey)) {
      throw new Error(`${where} names ${workKey} a second time`);
    }
    seen.add(workKey);
    if (requireOwn(entry, 'reason', where) !== 'ambiguous_version') {
      throw new Error(`${where} is ambiguous_version: several states apply on the date and none is picked`);
    }
    const candidates = requireTexts(requireOwn(entry, 'candidates', where), `${where} candidates`, { atLeast: 2 });
    for (const candidate of candidates) {
      const match = STATE_PERMALINK.exec(candidate);
      if (match === null || match[1] !== workKey || !isCalendarDate(match[2]) || match[2] > requestedDate) {
        throw new Error(`${where} names ${candidate}, which is not a state of ${workKey} applying by ${requestedDate}`);
      }
    }
    if (hits.some((hit) => hit.workKey === workKey)) {
      throw new Error(`${where} is ${workKey}, which contributes hits; an ambiguous work contributes none`);
    }
    return Object.freeze({ workKey, candidates });
  }));
}

function readHit(hit, index) {
  const where = `hit ${index + 1}`;
  const lane = requireOwn(hit, 'lane', where);
  if (!SEARCH_LANES.includes(lane)) {
    throw new Error(`${where} is in lane ${JSON.stringify(lane)}; the index holds ${SEARCH_LANES.join(' and ')}`);
  }

  const reasons = requireList(requireOwn(hit, 'match_reasons', where), `${where} match_reasons`);
  if (reasons.length !== 1 || reasons[0] !== LANE_MATCH_REASON[lane]) {
    throw new Error(
      `${where} is a ${lane} hit whose match reasons are ${JSON.stringify(reasons)}; a ${lane} hit matched by `
        + `${LANE_MATCH_REASON[lane]} and nothing else`,
    );
  }

  const workKey = requireText(requireOwn(hit, 'work_key', where), `${where} work_key`);
  const applicabilityDate = requireDate(requireOwn(hit, 'applicability_date', where), `${where} applicability_date`);
  const stateSha256 = requireDigest(requireOwn(hit, 'state_sha256', where), `${where} state_sha256`);
  const resolve = requireOwn(hit, 'resolve', where);
  const permalink = requireText(requireOwn(resolve, 'identifier', `${where} resolve`), `${where} resolve.identifier`);
  const expected = `/lu-legilux/${workKey}/${applicabilityDate}--${stateSha256}`;
  if (permalink !== expected) {
    throw new Error(`${where} resolves to ${permalink}, not to the state it names (${expected})`);
  }

  return Object.freeze({
    lane,
    matchReason: reasons[0],
    workKey,
    publisherWorkIri: requireText(requireOwn(hit, 'publisher_work_iri', where), `${where} publisher_work_iri`),
    language: requireText(requireOwn(hit, 'language', where), `${where} language`),
    applicabilityDate,
    articleIdentitySha256: requireDigest(requireOwn(hit, 'article_identity_sha256', where), `${where} article_identity_sha256`),
    publisherId: requireText(requireOwn(hit, 'publisher_id', where), `${where} publisher_id`),
    publisherWid: requireTextOrNull(requireOwn(hit, 'publisher_wid', where), `${where} publisher_wid`),
    stateSha256,
    permalink,
  });
}

/** The request an answer echoes, as both publishers' answers echo it: the mode, the cursor, the query and its terms. */
function readRequest(answer, where, cursor, cursorShape) {
  const mode = requireTextOrNull(requireOwn(answer, 'requested_mode', where), 'requested_mode');
  if (mode !== null && !SEARCH_LANES.includes(mode)) {
    throw new Error(`requested_mode ${JSON.stringify(mode)} is not a lane the index holds`);
  }
  const after = requireTextOrNull(requireOwn(answer, 'requested_after', where), 'requested_after');
  if (after !== null && !cursor.test(after)) {
    throw new Error(`requested_after ${JSON.stringify(after)} is not a cursor (${cursorShape})`);
  }
  const query = requireText(requireOwn(answer, 'requested_query', where), 'requested_query');
  const terms = requireList(requireOwn(answer, 'terms', where), 'terms')
    .map((term, index) => requireText(term, `terms[${index}]`));
  if (terms.length === 0) {
    throw new Error('terms names at least one term of the query');
  }
  return { mode, after, query, terms: Object.freeze(terms) };
}

/** Each article once on a page, by the key that names it there. */
function requireEachArticleOnce(hits, keyOf, twice) {
  const seen = new Set();
  for (const hit of hits) {
    const key = keyOf(hit);
    if (seen.has(key)) {
      throw new Error(twice(hit));
    }
    seen.add(key);
  }
}

/**
 * The rules both publishers' answers state about their lanes, their page and their population: strict
 * before relaxed, a mode's hits all in its lane, the limit, a cursor exactly when the page is truncated
 * and naming its last hit (`cursorOf`), and a population that counts the whole result, not the page.
 */
function readPage(answer, where, hits, { mode, after, cursorOf }) {
  const firstRelaxed = hits.findIndex((hit) => hit.lane === 'relaxed');
  if (firstRelaxed >= 0 && hits.slice(firstRelaxed).some((hit) => hit.lane === 'strict')) {
    throw new Error('a strict hit follows a relaxed one; the answer says relaxed never outranks strict');
  }
  if (mode !== null && hits.some((hit) => hit.lane !== mode)) {
    throw new Error(`a search in the ${mode} lane alone serves only ${mode} hits`);
  }

  const limit = requireCount(requireOwn(answer, 'limit', where), 'limit');
  if (hits.length > limit) {
    throw new Error(`the page holds ${hits.length} hits against a limit of ${limit}`);
  }

  const truncated = requireBoolean(requireOwn(answer, 'truncated', where), 'truncated');
  const continueAfter = requireTextOrNull(requireOwn(answer, 'continue_after', where), 'continue_after');
  if (truncated !== (continueAfter !== null)) {
    throw new Error('a page carries a cursor exactly when it is truncated');
  }

  if (truncated && hits.length === 0) {
    throw new Error('a truncated page holds at least the hits it stopped after');
  }

  if (continueAfter !== null && continueAfter !== cursorOf(hits[hits.length - 1])) {
    throw new Error('the cursor is not the last hit on the page');
  }

  const populationValue = requireOwn(answer, 'population', where);
  const population = Object.freeze({
    strictHits: readLaneCount(populationValue, 'strict', mode),
    relaxedHits: readLaneCount(populationValue, 'relaxed', mode),
    distinctPublisherArticles: requireCount(
      requireOwn(populationValue, 'distinct_publisher_articles', 'population'), 'population.distinct_publisher_articles'),
    worksWithHits: requireCount(requireOwn(populationValue, 'works_with_hits', 'population'), 'population.works_with_hits'),
  });
  // The population counts the whole result, not the page: an untruncated first page holds all of it,
  // a page after a cursor leaves out at least the hit the cursor names, and a truncated page leaves
  // out at least the hit it stops before.
  const total = (population.strictHits ?? 0) + (population.relaxedHits ?? 0);
  if (after === null && !truncated && total !== hits.length) {
    throw new Error(`the population counts ${total} hits and the untruncated first page holds ${hits.length}`);
  }

  const atLeast = hits.length + (after !== null ? 1 : 0) + (truncated ? 1 : 0);
  if (total < atLeast) {
    throw new Error(
      `a page of ${hits.length} hits${after !== null ? ' after a cursor' : ''}${truncated ? ' that stops before more' : ''} `
        + `comes from a population of at least ${atLeast}, not ${total}`,
    );
  }

  return { limit, truncated, continueAfter, population, total, populationValue };
}

/** What both publishers' answers say about how they searched, which index they searched and what it holds. */
function readMethod(answer, where) {
  return {
    matching: requireText(requireOwn(answer, 'matching', where), 'matching'),
    ranking: requireText(requireOwn(answer, 'ranking', where), 'ranking'),
    pageIs: requireText(requireOwn(answer, 'page_is', where), 'page_is'),
    hitUnit: requireText(requireOwn(answer, 'hit_unit', where), 'hit_unit'),
    lanes: requireText(requireOwn(answer, 'lanes', where), 'lanes'),
    modesNotHeld: Object.freeze(requireList(requireOwn(answer, 'modes_not_held', where), 'modes_not_held')
      .map((mode, index) => requireText(mode, `modes_not_held[${index}]`))),
    searchableTextHeld: requireBoolean(
      requireOwn(answer, 'searchable_text_held_for_language', where), 'searchable_text_held_for_language'),
    searchableLanguages: requireTexts(requireOwn(answer, 'searchable_languages', where), 'searchable_languages'),
    corpusSha256: requireDigest(requireOwn(answer, 'corpus_sha256', where), 'corpus_sha256'),
    indexSha256: requireDigest(requireOwn(answer, 'index_sha256', where), 'index_sha256'),
  };
}

/**
 * Reads one V3 Luxembourg `search` answer into the view a live screen renders.
 *
 * @param {object} answer the `result.value` of a search envelope
 * @returns {object} a frozen view; throws on any answer the rules above do not allow
 */
export function readSearch(answer) {
  const where = 'this search answer';
  const publisher = requireOwn(answer, 'publisher', where);
  if (publisher !== 'lu-legilux') {
    throw new Error(`this search answer is ${JSON.stringify(publisher)}'s; this reader reads the Luxembourg (lu-legilux) search answer only`);
  }
  const { mode, after, query, terms } = readRequest(answer, where, CURSOR, 'lane.state.article');
  const requestedDate = requireOwn(answer, 'requested_date', where);
  if (requestedDate !== null) requireDate(requestedDate, 'requested_date');
  const identifier = requireTextOrNull(requireOwn(answer, 'requested_identifier', where), 'requested_identifier');

  const hits = requireList(requireOwn(answer, 'hits', where), 'hits').map(readHit);
  requireEachArticleOnce(
    hits,
    (hit) => `${hit.stateSha256}.${hit.articleIdentitySha256}`,
    (hit) => `the page lists the article ${hit.publisherId} of one state twice; each article is served once`,
  );
  const { limit, truncated, continueAfter, population } = readPage(answer, where, hits, {
    mode,
    after,
    cursorOf: (last) => `${last.lane}.${last.stateSha256}.${last.articleIdentitySha256}`,
  });

  return Object.freeze({
    publisher,
    query,
    terms,
    language: requireText(requireOwn(answer, 'requested_language', where), 'requested_language'),
    mode,
    after,
    date: requestedDate,
    identifier,
    hits: Object.freeze(hits),
    ambiguousWorks: readAmbiguousWorks(requireOwn(answer, 'ambiguous_works', where), requestedDate, hits),
    workResolution: readWorkResolution(requireOwn(answer, 'work_resolution', where), identifier),
    limit,
    truncated,
    continueAfter,
    population,
    ...readMethod(answer, where),
  });
}

/**
 * A provision escaped as the platform escapes it (.NET's `Uri.EscapeDataString`, RFC 3986's unreserved
 * characters only): `encodeURIComponent` leaves `!'()*` as they are, and the platform does not.
 */
export function escapeProvision(provision) {
  return encodeURIComponent(provision).replace(/[!'()*]/g, (character) => `%${character.charCodeAt(0).toString(16).toUpperCase()}`);
}

/** The one wording an EU answer's hits are in, pinned; null when the answer pins none. */
function readPinnedWording(value, language) {
  if (value === null) return null;
  const where = 'pinned_wording';
  const wordingDate = requireDate(requireOwn(value, 'wording_date', where), `${where}.wording_date`);
  const wordingSha256 = requireDigest(requireOwn(value, 'wording_sha256', where), `${where}.wording_sha256`);
  const permalink = requireText(requireOwn(value, 'permalink', where), `${where}.permalink`);
  const match = EUROPE_WORDING_PERMALINK.exec(permalink);
  if (match === null) {
    throw new Error(`${where}.permalink ${JSON.stringify(permalink)} is not an EU wording permalink (/eu-eurlex/{celex}/{language}/{wording date}--{wording sha256})`);
  }
  const [, celex, permalinkLanguage, permalinkDate, permalinkSha256] = match;
  if (permalinkLanguage !== language) {
    throw new Error(`${where}.permalink is in ${permalinkLanguage}, and the search was asked in ${language}`);
  }
  if (permalinkDate !== wordingDate || permalinkSha256 !== wordingSha256) {
    throw new Error(`${where}.permalink pins ${permalinkDate}--${permalinkSha256}, not the wording it names (${wordingDate}--${wordingSha256})`);
  }
  return Object.freeze({
    celex,
    wordingDate,
    wordingSha256,
    permalink,
    digestRule: requireText(requireOwn(value, 'digest_rule', where), `${where}.digest_rule`),
  });
}

function readEuropeHit(hit, index, { language, wording }) {
  const where = `hit ${index + 1}`;
  const lane = requireOwn(hit, 'lane', where);
  if (!SEARCH_LANES.includes(lane)) {
    throw new Error(`${where} is in lane ${JSON.stringify(lane)}; the index holds ${SEARCH_LANES.join(' and ')}`);
  }

  const reasons = requireList(requireOwn(hit, 'match_reasons', where), `${where} match_reasons`);
  if (reasons.length !== 1 || reasons[0] !== LANE_MATCH_REASON[lane]) {
    throw new Error(
      `${where} is a ${lane} hit whose match reasons are ${JSON.stringify(reasons)}; a ${lane} hit matched by `
        + `${LANE_MATCH_REASON[lane]} and nothing else`,
    );
  }

  const hitLanguage = requireText(requireOwn(hit, 'language', where), `${where} language`);
  if (hitLanguage !== language) {
    throw new Error(`${where} is in ${hitLanguage}, and an EU search is in the one language asked (${language})`);
  }
  const expressionIri = requireText(requireOwn(hit, 'publisher_expression_id', where), `${where} publisher_expression_id`);
  const publisherId = requireText(requireOwn(hit, 'publisher_id', where), `${where} publisher_id`);
  const coordinate = requireText(requireOwn(requireOwn(hit, 'resolve', where), 'identifier', `${where} resolve`), `${where} resolve.identifier`);
  const expectedCoordinate = `${expressionIri}#lex-provision=${escapeProvision(publisherId)}`;
  if (coordinate !== expectedCoordinate) {
    throw new Error(`${where} resolves to ${coordinate}, not to the provision it names (${expectedCoordinate})`);
  }

  const celex = requireText(requireOwn(hit, 'celex', where), `${where} celex`);
  const wordingDate = requireDate(requireOwn(hit, 'wording_date', where), `${where} wording_date`);
  const permalink = requireOwn(hit, 'permalink', where);
  if (wording === null) {
    // A hit this page shows must pin its wording; an answer that pins none is not shown unpinned.
    throw new Error(`${where} pins no wording (the answer's pinned_wording is null), and a hit this page shows must pin its wording`);
  }
  if (celex !== wording.celex || wordingDate !== wording.wordingDate) {
    throw new Error(`${where} is in ${celex} of ${wordingDate}, not in the wording the answer pins (${wording.celex} of ${wording.wordingDate})`);
  }
  const expectedPermalink = `${wording.permalink}#${escapeProvision(publisherId)}`;
  if (permalink !== expectedPermalink) {
    throw new Error(`${where} carries the permalink ${JSON.stringify(permalink)}, not the pinned wording's with its provision (${expectedPermalink})`);
  }

  return Object.freeze({
    lane,
    matchReason: reasons[0],
    celex,
    publisherWorkIri: requireText(requireOwn(hit, 'publisher_work_id', where), `${where} publisher_work_id`),
    publisherExpressionIri: expressionIri,
    language: hitLanguage,
    wordingDate,
    articleIdentitySha256: requireDigest(requireOwn(hit, 'article_identity_sha256', where), `${where} article_identity_sha256`),
    publisherId,
    heading: requireText(requireOwn(hit, 'heading', where), `${where} heading`),
    coordinate,
    permalink,
  });
}

function readNotHeld(value, where) {
  return Object.freeze(requireList(value, where).map((row, index) => Object.freeze({
    item: requireText(requireOwn(row, 'item', `${where}[${index}]`), `${where}[${index}].item`),
    reason: requireText(requireOwn(row, 'reason', `${where}[${index}]`), `${where}[${index}].reason`),
  })));
}

/**
 * Reads one V3 EU `search` answer (one named EU work, the one wording the EU index holds of it) into the
 * view a live screen renders.
 *
 * @param {object} answer the `result.value` of a search envelope
 * @returns {object} a frozen view; throws on any answer the rules above do not allow
 */
export function readEuropeSearch(answer) {
  const where = 'this search answer';
  const publisher = requireOwn(answer, 'publisher', where);
  if (publisher !== 'eu-eurlex') {
    throw new Error(`this search answer is ${JSON.stringify(publisher)}'s; this reader reads the EU (eu-eurlex) search answer only`);
  }
  const { mode, after, query, terms } = readRequest(answer, where, EUROPE_CURSOR, 'lane.article');
  if (requireOwn(answer, 'requested_date', where) !== null) {
    throw new Error('an EU search names no date: the index holds one wording of the act and no consolidation');
  }
  const identifier = requireText(requireOwn(answer, 'requested_identifier', where), 'requested_identifier');
  const language = requireText(requireOwn(answer, 'requested_language', where), 'requested_language');
  const wording = readPinnedWording(requireOwn(answer, 'pinned_wording', where), language);

  const hits = requireList(requireOwn(answer, 'hits', where), 'hits')
    .map((hit, index) => readEuropeHit(hit, index, { language, wording }));
  const expressions = new Set(hits.map((hit) => hit.publisherExpressionIri));
  if (expressions.size > 1) {
    throw new Error(`the hits are in ${expressions.size} expressions, and an EU search is in the one wording of one expression`);
  }
  requireEachArticleOnce(
    hits,
    (hit) => hit.articleIdentitySha256,
    (hit) => `the page lists the article ${hit.publisherId} of the wording twice; each article is served once`,
  );
  const { limit, truncated, continueAfter, population, total, populationValue } = readPage(answer, where, hits, {
    mode,
    after,
    cursorOf: (last) => `${last.lane}.${last.articleIdentitySha256}`,
  });
  if (population.worksWithHits !== (total > 0 ? 1 : 0)) {
    throw new Error(`the population counts ${population.worksWithHits} works with hits, and an EU search is in one work`);
  }
  const scope = requireOwn(populationValue, 'scope', 'population');
  for (const [key, expected] of [['identifier', identifier], ['language', language], ['date', null], ['mode', mode]]) {
    if (requireOwn(scope, key, 'population.scope') !== expected) {
      throw new Error(`population.scope.${key} is ${JSON.stringify(scope[key])}, not what the search asked (${JSON.stringify(expected)})`);
    }
  }
  if (requireList(requireOwn(answer, 'ambiguous_works', where), 'ambiguous_works').length > 0) {
    throw new Error('a work is ambiguous only on a date, and an EU search names none');
  }

  return Object.freeze({
    publisher,
    query,
    terms,
    language,
    mode,
    after,
    identifier,
    wording,
    hits: Object.freeze(hits),
    workResolution: readWorkResolution(requireOwn(answer, 'work_resolution', where), identifier),
    limit,
    truncated,
    continueAfter,
    population,
    scope: requireText(requireOwn(answer, 'scope', where), 'scope'),
    dateSemantics: requireText(requireOwn(answer, 'date_semantics', where), 'date_semantics'),
    consolidationsHeld: requireBoolean(requireOwn(answer, 'consolidations_held', where), 'consolidations_held'),
    notHeld: readNotHeld(requireOwn(answer, 'not_held', where), 'not_held'),
    ...readMethod(answer, where),
  });
}

/** Reads a search answer with the reader its publisher names: Luxembourg's, or the EU's in one work. */
export function readSearchAnswer(answer) {
  return answer !== null && typeof answer === 'object' && answer.publisher === 'eu-eurlex'
    ? readEuropeSearch(answer)
    : readSearch(answer);
}
