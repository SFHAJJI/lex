// The V3 search answer, read.
//
// The search screen's renderer (`search-results.mjs`) was written before V3 against a shape the
// platform no longer sends: `lex_id`, `provision_num`, a row set and four match reasons. This file
// reads the answer the platform does send, held to the three answers captured by driving the real
// handler (`schemas/v3-platform/answer-samples.json`, operation `search`), and turns it into one
// view a live screen can render. It renders nothing itself.
//
// Every rule here is one the answer states about itself, so an answer that breaks one is drift and
// is thrown, never rendered: the lanes (strict before relaxed, and a hit's reason is its lane's);
// the population (the strict and relaxed counts add up to the hits of an untruncated page); the
// page (a cursor exactly when the page is truncated); and each hit's permalink (the work, the date
// and the state digest the hit names). A search answer carries no text snippet, so none is read.

import { isCalendarDate } from './temporal.mjs';

const DIGEST = /^[0-9a-f]{64}$/;

/** The two lanes the index holds, in the order a page serves them. */
export const SEARCH_LANES = Object.freeze(['strict', 'relaxed']);

/** A hit's one match reason is its lane's: the phrase as typed, or every term in any order. */
export const LANE_MATCH_REASON = Object.freeze({ strict: 'exact_phrase', relaxed: 'all_terms' });

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
    strictHits: requireCount(requireOwn(populationValue, 'strict_hits', 'population'), 'population.strict_hits'),
    relaxedHits: requireCount(requireOwn(populationValue, 'relaxed_hits', 'population'), 'population.relaxed_hits'),
    distinctPublisherArticles: requireCount(
      requireOwn(populationValue, 'distinct_publisher_articles', 'population'), 'population.distinct_publisher_articles'),
    worksWithHits: requireCount(requireOwn(populationValue, 'works_with_hits', 'population'), 'population.works_with_hits'),
  });
  const total = population.strictHits + population.relaxedHits;
  if (!truncated && total !== hits.length) {
    throw new Error(`the population counts ${total} hits and the untruncated page holds ${hits.length}`);
  }

  if (truncated && total <= hits.length) {
    throw new Error(`a truncated page of ${hits.length} hits comes from a population of more than that, not ${total}`);
  }

  return Object.freeze({
    query,
    terms: Object.freeze(terms),
    language: requireText(requireOwn(answer, 'requested_language', where), 'requested_language'),
    mode: requireTextOrNull(requireOwn(answer, 'requested_mode', where), 'requested_mode'),
    hits: Object.freeze(hits),
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
    corpusSha256: requireDigest(requireOwn(answer, 'corpus_sha256', where), 'corpus_sha256'),
    indexSha256: requireDigest(requireOwn(answer, 'index_sha256', where), 'index_sha256'),
  });
}
