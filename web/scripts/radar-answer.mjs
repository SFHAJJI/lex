// The V3 Luxembourg change radar, read.
//
// The radar screen reads the `changes_in_period` answer (object type `change_list`, publisher
// `lu-legilux`), held to the two answers captured by driving the real handler
// (`schemas/v3-platform/answer-samples.json`: a window holding a work's first held state, and one
// holding both states of a two-state work), and turns it into one view a live screen can render. It
// renders nothing itself.
//
// A row is a publisher-dated state in the window, the baseline it replaced, and whether the wording
// changed from it, or the reason that comparing would be dishonest. The reader holds every rule the
// answer states about its rows, and throws where one breaks:
//  - each row's state lies in the window, pinned by its permalink, which is also what `resolve`
//    serves; rows are in date order, then work;
//  - a compared row has one baseline of its work and language, dated before it and followed by it,
//    with the same rule profiles, a wording change that is exactly "an article changed, was added or
//    was removed", its counts, and the `diff` parameters that ask for that pair; it carries no reason
//    and no candidates;
//  - an uncompared row has no wording change, counts or `diff`, and one of the four reasons: the
//    first held state (no baseline), an ambiguous version or an ambiguous baseline (each with its
//    candidate states, and no baseline for the latter), or rule profiles that differ (with its
//    baseline, whose profiles differ);
//  - the page is truncated exactly when it names the first date not served, after its last row, and
//    an untruncated page holds every version the population counts in the window.
// Nothing is derived: no legal effect, no amending act, no entry into force.

import { isCalendarDate } from './temporal.mjs';

const DIGEST = /^[0-9a-f]{64}$/;

/** Why a row is not compared, in the platform's closed vocabulary. */
export const RADAR_REASONS = Object.freeze(['first_held_state', 'ambiguous_version', 'ambiguous_baseline', 'profiles_differ']);

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

/** A state named compactly, its permalink and stable coordinate checked against its work, date and digest. */
function readStateReference(state, where, workKey) {
  const applicabilityDate = requireDate(requireOwn(state, 'applicability_date', where), `${where} applicability_date`);
  const next = requireOwn(state, 'next_applicability_date', where);
  if (next !== null && requireDate(next, `${where} next_applicability_date`) <= applicabilityDate) {
    throw new Error(`${where} is followed from ${next}, which is not after its own date ${applicabilityDate}`);
  }
  const stateSha256 = requireDigest(requireOwn(state, 'state_sha256', where), `${where} state_sha256`);
  const coordinate = requireText(requireOwn(state, 'stable_coordinate', where), `${where} stable_coordinate`);
  if (coordinate !== `/lu-legilux/${workKey}/${applicabilityDate}`) {
    throw new Error(`${where}'s stable coordinate ${coordinate} does not name its work and date`);
  }
  const permalink = requireText(requireOwn(state, 'permalink', where), `${where} permalink`);
  if (permalink !== `${coordinate}--${stateSha256}`) {
    throw new Error(`${where}'s permalink ${permalink} does not pin its coordinate and digest`);
  }
  return Object.freeze({
    language: requireText(requireOwn(state, 'language', where), `${where} language`),
    applicabilityDate,
    nextApplicabilityDate: next,
    stateSha256,
    expressionIri: requireText(requireOwn(state, 'expression_iri', where), `${where} expression_iri`),
    articleCount: requireCount(requireOwn(state, 'article_count', where), `${where} article_count`),
    ruleProfileSha256s: Object.freeze(requireList(requireOwn(state, 'rule_profile_sha256s', where), `${where} rule_profile_sha256s`)
      .map((digest, index) => requireDigest(digest, `${where} rule_profile_sha256s[${index}]`))),
    stableCoordinate: coordinate,
    permalink,
  });
}

const sameProfiles = (a, b) => a.length === b.length && a.every((digest, index) => digest === b[index]);

function readRow(row, index, window) {
  const where = `row ${index + 1}`;
  const workKey = requireText(requireOwn(row, 'work_key', where), `${where} work_key`);
  const state = readStateReference(requireOwn(row, 'state', where), `${where} state`, workKey);
  if (state.applicabilityDate < window.from || state.applicabilityDate > window.to) {
    throw new Error(`${where}'s state is dated ${state.applicabilityDate}, outside the window ${window.from} to ${window.to}`);
  }
  const resolve = requireOwn(row, 'resolve', where);
  if (requireOwn(resolve, 'identifier', `${where} resolve`) !== state.permalink) {
    throw new Error(`${where} resolves to another state than the one it names`);
  }
  const baselineValue = requireOwn(row, 'baseline', where);
  const baseline = baselineValue === null ? null : readStateReference(baselineValue, `${where} baseline`, workKey);
  if (baseline !== null) {
    if (baseline.language !== state.language) throw new Error(`${where}'s baseline is in ${baseline.language}, its state in ${state.language}`);
    if (!(baseline.applicabilityDate < state.applicabilityDate)) throw new Error(`${where}'s baseline is not dated before its state`);
    if (baseline.nextApplicabilityDate !== state.applicabilityDate) {
      throw new Error(`${where}'s baseline is followed from ${baseline.nextApplicabilityDate}, not by the state it is the baseline of`);
    }
  }
  const wordingChanged = requireOwn(row, 'wording_changed', where);
  const reason = requireOwn(row, 'reason', where);
  const candidatesValue = requireOwn(row, 'candidates', where);
  const countsValue = requireOwn(row, 'counts', where);
  const diffValue = requireOwn(row, 'diff', where);
  const common = {
    workKey,
    publisherWorkIri: requireText(requireOwn(row, 'publisher_work_iri', where), `${where} publisher_work_iri`),
    state,
    baseline,
  };

  if (reason === null) {
    requireBoolean(wordingChanged, `${where} wording_changed`);
    if (baseline === null) throw new Error(`${where} is compared, so it has the one baseline it was compared with`);
    if (candidatesValue !== null) throw new Error(`${where} is compared, and names no candidates`);
    if (!sameProfiles(baseline.ruleProfileSha256s, state.ruleProfileSha256s)) {
      throw new Error(`${where} compares states read under different rule profiles, which the platform never compares`);
    }
    const counts = Object.fromEntries(['unchanged', 'changed', 'added', 'removed'].map((status) => [
      status, requireCount(requireOwn(countsValue, status, `${where} counts`), `${where} counts.${status}`),
    ]));
    if (wordingChanged !== (counts.changed + counts.added + counts.removed > 0)) {
      throw new Error(`${where} says wording_changed is ${wordingChanged}, and its counts say otherwise`);
    }
    const diff = {
      identifier: requireOwn(diffValue, 'identifier', `${where} diff`),
      dateFrom: requireOwn(diffValue, 'date_from', `${where} diff`),
      dateTo: requireOwn(diffValue, 'date_to', `${where} diff`),
      language: requireOwn(diffValue, 'language', `${where} diff`),
    };
    if (diff.identifier !== `/lu-legilux/${workKey}` || diff.dateFrom !== baseline.applicabilityDate
      || diff.dateTo !== state.applicabilityDate || diff.language !== state.language) {
      throw new Error(`${where}'s diff parameters do not ask for its baseline and its state`);
    }
    return Object.freeze({ ...common, wordingChanged, reason: null, candidates: null, counts: Object.freeze(counts), diff: Object.freeze(diff) });
  }

  if (!RADAR_REASONS.includes(reason)) {
    throw new Error(`${where}'s reason ${JSON.stringify(reason)} is not one of ${RADAR_REASONS.join(', ')}`);
  }
  if (wordingChanged !== null || countsValue !== null || diffValue !== null) {
    throw new Error(`${where} is not compared (${reason}), so it carries no wording change, counts or diff`);
  }
  const listsCandidates = reason === 'ambiguous_version' || reason === 'ambiguous_baseline';
  if ((candidatesValue !== null) !== listsCandidates) {
    throw new Error(`${where} ${listsCandidates ? 'names' : 'names no'} candidate states for ${reason}`);
  }
  const candidates = candidatesValue === null ? null : Object.freeze(requireList(candidatesValue, `${where} candidates`)
    .map((candidate, candidateIndex) => requireText(candidate, `${where} candidates[${candidateIndex}]`)));
  if (candidates !== null && candidates.length < 2) throw new Error(`${where}'s ambiguity names at least two candidate states`);
  if (reason === 'ambiguous_version' && !candidates.includes(state.permalink)) {
    throw new Error(`${where} is ambiguous on its own date, so its own state is among the candidates`);
  }
  if (reason === 'first_held_state' && baseline !== null) throw new Error(`${where} is the first held state, with no baseline`);
  if (reason === 'ambiguous_baseline' && baseline !== null) throw new Error(`${where}'s baseline is ambiguous, so none is named`);
  if (reason === 'profiles_differ') {
    if (baseline === null) throw new Error(`${where}'s profiles differ from its baseline, which it names`);
    if (sameProfiles(baseline.ruleProfileSha256s, state.ruleProfileSha256s)) {
      throw new Error(`${where} says the profiles differ, and its baseline's profiles are its state's`);
    }
  }
  return Object.freeze({ ...common, wordingChanged: null, reason, candidates, counts: null, diff: null });
}

/**
 * Reads one V3 Luxembourg `changes_in_period` answer into the view a live radar screen renders.
 *
 * @param {object} answer the `result.value` of a changes_in_period envelope
 * @returns {object} a frozen view; throws on any answer the rules above do not allow
 */
export function readChanges(answer) {
  const where = 'this change radar';
  const publisher = requireOwn(answer, 'publisher', where);
  if (publisher !== 'lu-legilux') {
    throw new Error(`this change radar is ${JSON.stringify(publisher)}'s; this reader reads the Luxembourg (lu-legilux) radar only`);
  }
  const requestedFrom = requireDate(requireOwn(answer, 'requested_date_from', where), 'requested_date_from');
  const requestedTo = requireDate(requireOwn(answer, 'requested_date_to', where), 'requested_date_to');
  const window = {
    from: requireDate(requireOwn(answer, 'window_from', where), 'window_from'),
    to: requireDate(requireOwn(answer, 'window_to', where), 'window_to'),
  };
  const [low, high] = requestedFrom <= requestedTo ? [requestedFrom, requestedTo] : [requestedTo, requestedFrom];
  if (window.from !== low || window.to !== high) {
    throw new Error('the window is the closed interval between the two dates, whichever is given first');
  }

  const rows = requireList(requireOwn(answer, 'changes', where), 'changes').map((row, index) => readRow(row, index, window));
  for (let index = 1; index < rows.length; index += 1) {
    const [a, b] = [rows[index - 1], rows[index]];
    if (b.state.applicabilityDate < a.state.applicabilityDate
      || (b.state.applicabilityDate === a.state.applicabilityDate && b.workKey < a.workKey)) {
      throw new Error('the rows are in publisher date order, then work order');
    }
  }

  const limit = requireCount(requireOwn(answer, 'limit', where), 'limit');
  const truncated = requireBoolean(requireOwn(answer, 'truncated', where), 'truncated');
  const continueFrom = requireOwn(answer, 'continue_from', where);
  if (truncated !== (continueFrom !== null)) throw new Error('a page names the first date not served exactly when it is truncated');
  if (continueFrom !== null) {
    requireDate(continueFrom, 'continue_from');
    if (rows.length > 0 && !(continueFrom > rows[rows.length - 1].state.applicabilityDate)) {
      throw new Error('the first date not served comes after the page\'s last row');
    }
  }
  const wholeDateOverLimit = requireBoolean(requireOwn(answer, 'whole_date_over_limit', where), 'whole_date_over_limit');
  if (wholeDateOverLimit !== (rows.length > limit)) {
    throw new Error('whole_date_over_limit says exactly that one date held more rows than the limit, and was served whole');
  }

  const populationValue = requireOwn(answer, 'population', where);
  const population = Object.freeze({
    worksHeld: requireCount(requireOwn(populationValue, 'works_held', 'population'), 'population.works_held'),
    worksInWindow: requireCount(requireOwn(populationValue, 'works_in_window', 'population'), 'population.works_in_window'),
    versionsInWindow: requireCount(requireOwn(populationValue, 'versions_in_window', 'population'), 'population.versions_in_window'),
    firstDateHeld: requireOwn(populationValue, 'first_date_held', 'population'),
    lastDateHeld: requireOwn(populationValue, 'last_date_held', 'population'),
    windowOverlapsWhatIsHeld: requireBoolean(
      requireOwn(populationValue, 'window_overlaps_what_is_held', 'population'), 'population.window_overlaps_what_is_held'),
  });
  if (!truncated) {
    if (population.versionsInWindow !== rows.length) {
      throw new Error(`the population counts ${population.versionsInWindow} versions in the window, and the untruncated page holds ${rows.length}`);
    }
    if (population.worksInWindow !== new Set(rows.map((row) => row.workKey)).size) {
      throw new Error('the population counts other works in the window than the untruncated page holds');
    }
  }
  if (population.worksInWindow > population.worksHeld) throw new Error('more works in the window than works held');
  const overlaps = population.firstDateHeld !== null && population.lastDateHeld !== null
    && window.from <= population.lastDateHeld && window.to >= population.firstDateHeld;
  if (population.windowOverlapsWhatIsHeld !== overlaps) {
    throw new Error('window_overlaps_what_is_held is not whether the window meets the held dates');
  }

  return Object.freeze({
    requestedFrom,
    requestedTo,
    window: Object.freeze(window),
    identifier: requireTextOrNull(requireOwn(answer, 'requested_identifier', where), 'requested_identifier'),
    language: requireTextOrNull(requireOwn(answer, 'requested_language', where), 'requested_language'),
    rows: Object.freeze(rows),
    limit,
    truncated,
    continueFrom,
    wholeDateOverLimit,
    population,
    caveat: requireText(requireOwn(answer, 'caveat', where), 'caveat'),
    wordingRule: requireText(requireOwn(answer, 'wording_rule', where), 'wording_rule'),
    corpusSha256: requireDigest(requireOwn(answer, 'corpus_sha256', where), 'corpus_sha256'),
    indexSha256: requireDigest(requireOwn(answer, 'index_sha256', where), 'index_sha256'),
  });
}
