// The V3 Luxembourg search answer, read.
//
// The search screen's renderer (`search-results.mjs`) was written before V3 against a shape the
// platform no longer sends: `lex_id`, `provision_num`, a row set and four match reasons. This file
// reads the answer the platform does send for a Luxembourg search (`publisher: "lu-legilux"`; an EU
// search, answered for one EU work, has another shape and is refused here), held to the answers
// captured by driving the real handler (`schemas/v3-platform/answer-samples.json`, operation
// `search`), and turns it into one view a live screen can render. It renders nothing itself.
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

/**
 * Reads one V3 `search` answer into the view a live screen renders.
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
  const mode = requireTextOrNull(requireOwn(answer, 'requested_mode', where), 'requested_mode');
  if (mode !== null && !SEARCH_LANES.includes(mode)) {
    throw new Error(`requested_mode ${JSON.stringify(mode)} is not a lane the index holds`);
  }
  const after = requireTextOrNull(requireOwn(answer, 'requested_after', where), 'requested_after');
  if (after !== null && !CURSOR.test(after)) {
    throw new Error(`requested_after ${JSON.stringify(after)} is not a cursor (lane.state.article)`);
  }
  const requestedDate = requireOwn(answer, 'requested_date', where);
  if (requestedDate !== null) requireDate(requestedDate, 'requested_date');
  const identifier = requireTextOrNull(requireOwn(answer, 'requested_identifier', where), 'requested_identifier');
  const query = requireText(requireOwn(answer, 'requested_query', where), 'requested_query');
  const terms = requireList(requireOwn(answer, 'terms', where), 'terms')
    .map((term, index) => requireText(term, `terms[${index}]`));
  if (terms.length === 0) {
    throw new Error('terms names at least one term of the query');
  }

  const hits = requireList(requireOwn(answer, 'hits', where), 'hits').map(readHit);
  const firstRelaxed = hits.findIndex((hit) => hit.lane === 'relaxed');
  if (firstRelaxed >= 0 && hits.slice(firstRelaxed).some((hit) => hit.lane === 'strict')) {
    throw new Error('a strict hit follows a relaxed one; the answer says relaxed never outranks strict');
  }
  if (mode !== null && hits.some((hit) => hit.lane !== mode)) {
    throw new Error(`a search in the ${mode} lane alone serves only ${mode} hits`);
  }

  const seen = new Set();
  for (const hit of hits) {
    const key = `${hit.stateSha256}.${hit.articleIdentitySha256}`;
    if (seen.has(key)) {
      throw new Error(`the page lists the article ${hit.publisherId} of one state twice; each article is served once`);
    }
    seen.add(key);
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

  if (continueAfter !== null) {
    const last = hits[hits.length - 1];
    if (continueAfter !== `${last.lane}.${last.stateSha256}.${last.articleIdentitySha256}`) {
      throw new Error('the cursor is not the last hit on the page');
    }
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

  return Object.freeze({
    query,
    terms: Object.freeze(terms),
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
  });
}
